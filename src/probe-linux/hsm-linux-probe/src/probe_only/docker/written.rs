//! `Disk written per hour`: the bytes a Compose service's containers wrote to block devices during
//! one clock hour — which container is wearing the disk.
//!
//! **Source.** `blkio_stats.io_service_bytes_recursive` of the one-shot stats the source already
//! reads every sample period: a cumulative counter per container, op `write` (cgroup v2, which
//! Docker fills from the cgroup's `io.stat`) or `Write` (cgroup v1), summed over devices. These are
//! writes that reached a block device: data still in the page cache counts when it is flushed, and
//! tmpfs never counts.
//!
//! **Accounting.** Every sample adds the container's delta since its previous sample to the
//! service's current clock hour (UTC). A delta that cannot be honest is dropped and the counter
//! becomes the new baseline: the first sample of a container (first sight, or a recreate — a new
//! id), a counter that went backwards, a clock that went backwards, and a gap longer than
//! [`contract::MAX_INTERVAL_FACTOR`] sample periods that crosses an hour boundary (its bytes cannot
//! be placed in either hour). A long gap inside one hour is kept: all of it belongs to that hour.
//! So a mid-hour recreate loses only the writes between the old container's last sample and the
//! new one's first, not the hour.
//!
//! **Posting.** An hour is posted on the first tick after it ends, and only when at least one delta
//! was accepted in it — never an invented 0. The value's time is when it is sent (just after the
//! hour); the comment names the window. The record — the running hour and each container's
//! baseline — lives in the state file, so a probe restart continues the hour, and the writes made
//! while the probe was down count too when the container and its counter survived.

use std::collections::BTreeMap;
use std::time::Duration;

use serde::{Deserialize, Serialize};

use super::contract;
use super::engine::ContainerStats;

const HOUR_MS: i64 = 3_600_000;

/// Unix seconds of the start of the clock hour (UTC) containing `unix_ms`.
pub fn hour_start(unix_ms: i64) -> i64 {
    unix_ms.div_euclid(HOUR_MS) * 3600
}

/// Bytes the container has written to block devices since it started; `None` when the stats carry
/// no write counter: a stopped container (`null`), one that has done no block I/O yet, or a host
/// that does not account block I/O per container — Docker Desktop answers `[]` for every
/// container (checked 2026-09-29, Engine 29.6.2 on WSL2), so no sensor registers there.
pub fn written_bytes(stats: &ContainerStats) -> Option<u64> {
    let entries = stats.blkio_stats.io_service_bytes_recursive.as_ref()?;
    let mut total: Option<u64> = None;
    for entry in entries {
        if entry.op.eq_ignore_ascii_case("write") {
            total = Some(total.unwrap_or(0).saturating_add(entry.value));
        }
    }
    total
}

/// A container's counter at its last sample.
#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Baseline {
    pub bytes: u64,
    /// Unix milliseconds of the sample.
    pub at_ms: i64,
}

/// Why a sample added nothing.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Skip {
    /// No earlier sample of this container (first sight, or a new container id).
    FirstSample,
    /// The counter went backwards.
    CounterReset,
    /// The wall clock went backwards.
    ClockBackwards,
    /// The probe missed samples across an hour boundary: the bytes cannot be placed in an hour.
    GapAcrossHours,
}

/// An hour that ended, with what was measured in it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct CompletedHour {
    /// Unix seconds.
    pub hour_start: i64,
    pub bytes: u64,
    /// Milliseconds of the hour the accepted deltas cover.
    pub covered_ms: u64,
}

impl CompletedHour {
    /// Decimal megabytes (10⁶ bytes, the unit SSD endurance is rated in), two decimals.
    pub fn megabytes(&self) -> f64 {
        (self.bytes as f64 / contract::BYTES_PER_DECIMAL_MB * 100.0).round() / 100.0
    }

    /// `13:00–14:00 UTC`, plus how much of the hour was measured when it was not all of it.
    pub fn comment(&self) -> String {
        let hour = self.hour_start.rem_euclid(86_400) / 3600;
        let window = format!("{hour:02}:00–{:02}:00 UTC", (hour + 1) % 24);
        let minutes = self.covered_ms / 60_000;
        if minutes >= 57 {
            window
        } else {
            format!("{window}; measured {minutes} of 60 min")
        }
    }
}

/// The running hour of one service, persisted with the service in the state file.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WriteRecord {
    /// Unix seconds of the start of the hour being accumulated.
    pub hour_start: i64,
    pub bytes: u64,
    pub covered_ms: u64,
    /// Accepted sample rounds in the hour; 0 ⇒ nothing to post.
    pub rounds: u32,
    /// Each current container's last counter.
    #[serde(default)]
    pub baselines: BTreeMap<String, Baseline>,
}

impl WriteRecord {
    pub fn new(now_ms: i64) -> Self {
        Self {
            hour_start: hour_start(now_ms),
            ..Self::default()
        }
    }

    /// Close the running hour when `now_ms` is past it and start the current one (baselines kept).
    /// Returns the closed hour when it has something honest to post.
    pub fn roll(&mut self, now_ms: i64) -> Option<CompletedHour> {
        let current = hour_start(now_ms);
        if self.hour_start >= current {
            return None;
        }
        let done = CompletedHour {
            hour_start: self.hour_start,
            bytes: self.bytes,
            covered_ms: self.covered_ms,
        };
        let rounds = self.rounds;
        self.hour_start = current;
        self.bytes = 0;
        self.covered_ms = 0;
        self.rounds = 0;
        (rounds > 0).then_some(done)
    }

    /// Record the container's counter read at `now_ms`; returns its delta and the interval it
    /// covers (ms), or why there is none. The new counter always becomes the baseline.
    pub fn sample(
        &mut self,
        container_id: &str,
        written: u64,
        now_ms: i64,
        sample_period: Duration,
    ) -> Result<(u64, u64), Skip> {
        let current = Baseline {
            bytes: written,
            at_ms: now_ms,
        };
        let previous = self
            .baselines
            .insert(container_id.to_string(), current)
            .ok_or(Skip::FirstSample)?;
        if written < previous.bytes {
            return Err(Skip::CounterReset);
        }
        if now_ms < previous.at_ms {
            return Err(Skip::ClockBackwards);
        }
        let gap = u64::try_from(now_ms - previous.at_ms).unwrap_or(u64::MAX);
        let longest = sample_period.as_secs_f64() * 1000.0 * contract::MAX_INTERVAL_FACTOR;
        if gap as f64 > longest && hour_start(previous.at_ms) != hour_start(now_ms) {
            return Err(Skip::GapAcrossHours);
        }
        Ok((written - previous.bytes, gap))
    }

    /// Add one round of the service (its replicas' deltas summed) to the running hour. `gap_ms` is
    /// the longest interval among them; coverage never exceeds the part of the hour that passed.
    pub fn credit(&mut self, bytes: u64, gap_ms: u64, now_ms: i64) {
        let into_hour = u64::try_from(now_ms - self.hour_start * 1000).unwrap_or(0);
        self.bytes = self.bytes.saturating_add(bytes);
        self.covered_ms = self
            .covered_ms
            .saturating_add(gap_ms.min(into_hour))
            .min(HOUR_MS as u64);
        self.rounds = self.rounds.saturating_add(1);
    }

    /// Forget one container's baseline (its counter is gone).
    pub fn forget(&mut self, container_id: &str) {
        self.baselines.remove(container_id);
    }

    /// Keep only the baselines of the service's current containers.
    pub fn retain(&mut self, ids: &[String]) {
        self.baselines
            .retain(|id, _| ids.iter().any(|listed| listed == id));
    }
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;

    use super::super::engine::tests::{STATS_T0, STATS_T1};
    use super::*;

    const PERIOD: Duration = Duration::from_secs(5);
    /// 2026-09-29 13:00:00 UTC, ms.
    const H13: i64 = 1_790_686_800_000;
    const MIN: i64 = 60_000;

    fn garage_stats(fixture: &str) -> HashMap<String, ContainerStats> {
        serde_json::from_str(fixture).unwrap()
    }

    fn by_prefix<'a>(
        stats: &'a HashMap<String, ContainerStats>,
        prefix: &str,
    ) -> &'a ContainerStats {
        stats
            .iter()
            .find(|(id, _)| id.starts_with(prefix))
            .unwrap()
            .1
    }

    #[test]
    fn hours_are_utc_clock_hours() {
        assert_eq!(hour_start(H13) * 1000, H13);
        assert_eq!(hour_start(H13 + 59 * MIN + 59_999) * 1000, H13);
        assert_eq!(hour_start(H13 + 60 * MIN) * 1000, H13 + 60 * MIN);
    }

    #[test]
    fn the_garage_write_counters_are_cgroup_v2_lowercase_writes() {
        let t0 = garage_stats(STATS_T0);
        let t1 = garage_stats(STATS_T1);
        // gitea/db on 8:32: 11_377_098_752 → 11_377_262_592 bytes written in 5.3 s; its reads
        // (2_881_179_648) are not counted.
        assert_eq!(
            written_bytes(by_prefix(&t0, "abbd59dcacc8")),
            Some(11_377_098_752)
        );
        assert_eq!(
            written_bytes(by_prefix(&t1, "abbd59dcacc8")),
            Some(11_377_262_592)
        );
        // The exited ci-image has `io_service_bytes_recursive: null`: no counter, not 0.
        assert_eq!(written_bytes(by_prefix(&t0, "3537ef2ee867")), None);
    }

    #[test]
    fn cgroup_v1_writes_are_summed_over_devices_and_total_is_ignored() {
        let stats: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 8, "minor": 0, "op": "Read", "value": 7 },
                { "major": 8, "minor": 0, "op": "Write", "value": 100 },
                { "major": 8, "minor": 0, "op": "Total", "value": 107 },
                { "major": 8, "minor": 16, "op": "Write", "value": 50 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&stats), Some(150));
        let empty: ContainerStats =
            serde_json::from_value(serde_json::json!({ "blkio_stats": {} })).unwrap();
        assert_eq!(written_bytes(&empty), None);
        // Docker Desktop's shape: every list present and empty.
        let desktop: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [], "sectors_recursive": [] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&desktop), None);
        let reads_only: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 8, "minor": 0, "op": "read", "value": 7 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&reads_only), None);
    }

    #[test]
    fn deltas_accumulate_into_the_hour_and_post_after_it() {
        let mut record = WriteRecord::new(H13);
        assert_eq!(
            record.sample("a", 1_000, H13 + 10_000, PERIOD),
            Err(Skip::FirstSample)
        );
        let mut at = H13 + 10_000;
        let mut counter = 1_000;
        // 2 MB every 5 s for the rest of the hour.
        while at + 5_000 < H13 + 60 * MIN {
            at += 5_000;
            counter += 2_000_000;
            let (delta, gap) = record.sample("a", counter, at, PERIOD).unwrap();
            record.credit(delta, gap, at);
        }
        assert_eq!(record.roll(H13 + 59 * MIN), None, "not over yet");
        let done = record.roll(H13 + 60 * MIN + 1_000).expect("posted");
        assert_eq!(done.hour_start * 1000, H13);
        assert_eq!(done.bytes, counter - 1_000);
        assert_eq!(done.comment(), "13:00–14:00 UTC");
        assert!(
            (done.megabytes() - 1_434.0).abs() < 0.01,
            "{}",
            done.megabytes()
        );
        // The new hour starts empty but keeps the baseline: its first sample counts.
        assert_eq!(record.bytes, 0);
        let (delta, _) = record
            .sample("a", counter + 5, H13 + 60 * MIN + 2_000, PERIOD)
            .unwrap();
        assert_eq!(delta, 5);
    }

    #[test]
    fn an_hour_without_an_accepted_delta_is_not_posted_as_zero() {
        let mut record = WriteRecord::new(H13);
        record.sample("a", 1_000, H13 + 59 * MIN, PERIOD).ok();
        assert_eq!(record.roll(H13 + 61 * MIN), None);
        assert_eq!(
            record.sample("a", 1_000, H13 + 61 * MIN, PERIOD),
            Err(Skip::GapAcrossHours)
        );
        // …but an hour of measured silence is a real 0.
        let at = H13 + 61 * MIN + 5_000;
        let (delta, gap) = record.sample("a", 1_000, at, PERIOD).unwrap();
        record.credit(delta, gap, at);
        let done = record.roll(H13 + 121 * MIN).unwrap();
        assert_eq!(done.megabytes(), 0.0);
    }

    #[test]
    fn a_counter_reset_or_a_new_container_id_loses_only_its_sample() {
        let mut record = WriteRecord::new(H13);
        record.sample("old", 5_000, H13, PERIOD).ok();
        let (delta, gap) = record.sample("old", 6_000, H13 + 5_000, PERIOD).unwrap();
        record.credit(delta, gap, H13 + 5_000);
        // Recreated: a new id with a fresh counter far below the old one — a baseline, not a
        // negative or a huge delta.
        assert_eq!(
            record.sample("new", 10, H13 + 10_000, PERIOD),
            Err(Skip::FirstSample)
        );
        record.retain(&["new".to_string()]);
        assert_eq!(record.baselines.len(), 1);
        let (delta, gap) = record.sample("new", 110, H13 + 15_000, PERIOD).unwrap();
        record.credit(delta, gap, H13 + 15_000);
        // Same id, counter backwards: a baseline again.
        assert_eq!(
            record.sample("new", 50, H13 + 20_000, PERIOD),
            Err(Skip::CounterReset)
        );
        let (delta, gap) = record.sample("new", 80, H13 + 25_000, PERIOD).unwrap();
        record.credit(delta, gap, H13 + 25_000);
        let done = record.roll(H13 + 60 * MIN).unwrap();
        assert_eq!(
            done.bytes,
            1_000 + 100 + 30,
            "the hour survives the recreate"
        );
    }

    #[test]
    fn a_gap_inside_the_hour_counts_and_one_across_hours_is_dropped() {
        let mut record = WriteRecord::new(H13);
        record.sample("a", 0, H13 + MIN, PERIOD).ok();
        // Ten minutes without a sample (the daemon did not answer), same hour: all of it counts.
        let (delta, gap) = record.sample("a", 700, H13 + 11 * MIN, PERIOD).unwrap();
        assert_eq!((delta, gap), (700, 10 * MIN as u64));
        record.credit(delta, gap, H13 + 11 * MIN);
        record.sample("a", 800, H13 + 55 * MIN, PERIOD).unwrap();
        // Probe down from 13:55 to 14:05: the bytes belong to either hour — dropped.
        record.roll(H13 + 65 * MIN);
        assert_eq!(
            record.sample("a", 900, H13 + 65 * MIN, PERIOD),
            Err(Skip::GapAcrossHours)
        );
        // An ordinary sample across the boundary is not a gap.
        let mut record = WriteRecord::new(H13);
        record.sample("a", 0, H13 + 60 * MIN - 2_000, PERIOD).ok();
        record.roll(H13 + 60 * MIN + 3_000);
        assert_eq!(
            record.sample("a", 10, H13 + 60 * MIN + 3_000, PERIOD),
            Ok((10, 5_000))
        );
        // The clock going backwards is a new baseline.
        assert_eq!(
            record.sample("a", 20, H13 + 50 * MIN, PERIOD),
            Err(Skip::ClockBackwards)
        );
    }

    #[test]
    fn a_partial_hour_says_how_much_of_it_was_measured() {
        // The probe started at 13:40.
        let mut record = WriteRecord::new(H13 + 40 * MIN);
        record.sample("a", 0, H13 + 40 * MIN, PERIOD).ok();
        let mut at = H13 + 40 * MIN;
        while at + 5_000 < H13 + 60 * MIN {
            at += 5_000;
            let (delta, gap) = record.sample("a", 1, at, PERIOD).unwrap();
            record.credit(delta, gap, at);
        }
        let done = record.roll(H13 + 60 * MIN + 1_000).unwrap();
        assert_eq!(done.comment(), "13:00–14:00 UTC; measured 19 of 60 min");
        // The first round of an hour covers only the part since the boundary.
        let mut record = WriteRecord::new(H13 + 60 * MIN);
        record.credit(0, 5_000, H13 + 60 * MIN + 3_000);
        assert_eq!(record.covered_ms, 3_000);
        // Midnight wraps.
        let late = CompletedHour {
            hour_start: (H13 + 10 * 60 * MIN) / 1000,
            bytes: 0,
            covered_ms: 3_600_000,
        };
        assert_eq!(late.comment(), "23:00–00:00 UTC");
    }

    #[test]
    fn the_record_round_trips_through_json() {
        let mut record = WriteRecord::new(H13);
        record.sample("abc", 42, H13 + 1_000, PERIOD).ok();
        record.credit(7, 5_000, H13 + 1_000);
        let text = serde_json::to_string(&record).unwrap();
        assert!(text.contains("\"hourStart\""), "{text}");
        let back: WriteRecord = serde_json::from_str(&text).unwrap();
        assert_eq!(back, record);
    }
}
