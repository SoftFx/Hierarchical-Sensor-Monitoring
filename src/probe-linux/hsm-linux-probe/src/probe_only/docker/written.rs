//! `Disk written per hour`: the bytes a Compose service's containers wrote to block devices during
//! one clock hour — which container is wearing the disk.
//!
//! **Source.** `blkio_stats.io_service_bytes_recursive` of the one-shot stats the source already
//! reads every sample period: a cumulative counter per container, op `write` (cgroup v2, which
//! Docker fills from the cgroup's `io.stat`) or `Write` (cgroup v1), summed over devices. These are
//! writes that reached a block device: data still in the page cache counts when it is flushed, and
//! tmpfs never counts. A write through a stacked device (LVM, dm-crypt, md) is accounted both on
//! that device and on the disk under it; a stacked device is therefore left out whenever a disk
//! under it is listed too ([`Stacking`]), so a write counts on the physical disks it reached: once
//! through LVM or dm-crypt, **once per member disk** through a mirror (md RAID1/10) — the physical
//! wear, which is what this sensor is for.
//!
//! **Accounting.** Every sample adds the container's delta since its previous sample to the
//! service's current clock hour (UTC). A delta that cannot be honest is dropped and the counter
//! becomes the new baseline: the first sample of a container (first sight, or a recreate — a new
//! id), a restart of the same container (a changed `State.StartedAt`: a new cgroup whose counter
//! began again from 0, even when it has already passed the old value), a counter that went
//! backwards, a clock that went backwards, and a gap longer than
//! [`contract::MAX_INTERVAL_FACTOR`] sample periods that crosses an hour boundary (its bytes cannot
//! be placed in either hour). A long gap inside one hour is kept: all of it belongs to that hour.
//! So a mid-hour recreate loses only the writes between the old container's last sample and the
//! new one's first, not the hour.
//!
//! **Posting.** An hour is posted on the first tick after it ends, and only when at least one delta
//! was accepted in it — never an invented 0. The value's time is when it is sent (just after the
//! hour); the comment names the window. An hour is posted once: a clock stepped back into the hour
//! just posted (a small NTP step across the boundary) does not reopen it — the running hour keeps
//! counting (#1489). The record — the running hour and each container's
//! baseline — lives in the state file, so a probe restart continues the hour, and the writes made
//! while the probe was down count too when the container and its counter survived.

use std::collections::{BTreeMap, HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::time::Duration;

use serde::{Deserialize, Serialize};

use super::contract;
use super::engine::ContainerStats;

const HOUR_MS: i64 = 3_600_000;

/// How long a start time made stale by a counter drop stays ignored while the poll keeps reporting
/// it: three state polls. Past that the poll has plainly caught up and the drop was not a restart
/// (a device leaving the counters), so the start time is trusted again.
const STALE_START_GRACE_MS: i64 = 3 * 60_000;

/// Unix seconds of the start of the clock hour (UTC) containing `unix_ms`.
pub fn hour_start(unix_ms: i64) -> i64 {
    unix_ms.div_euclid(HOUR_MS) * 3600
}

/// Which block devices are stacked on which, read from sysfs: `major:minor` → the whole disks
/// under it (`/sys/dev/block/<M:m>/slaves`, followed down to the bottom; a partition stands for its
/// disk). A plain disk has none. Cached; [`Stacking::refresh`] drops the cache (every state poll).
#[derive(Debug, Default)]
pub struct Stacking {
    sys_root: Option<PathBuf>,
    under: HashMap<String, Vec<String>>,
}

impl Stacking {
    /// `None`: nothing is known to be stacked (every device counts).
    pub fn new(sys_root: Option<PathBuf>) -> Self {
        Self {
            sys_root,
            under: HashMap::new(),
        }
    }

    pub fn refresh(&mut self) {
        self.under.clear();
    }

    /// The whole disks under `device` (`major:minor`); empty for a plain disk or when unknown.
    pub fn under(&mut self, device: &str) -> &[String] {
        let sys_root = self.sys_root.clone();
        self.under
            .entry(device.to_string())
            .or_insert_with(|| match &sys_root {
                Some(sys) => disks_under(sys, device, 0),
                None => Vec::new(),
            })
    }
}

/// Stacks deeper than this are not followed (a guard against a sysfs loop).
const MAX_STACK_DEPTH: usize = 8;

fn disks_under(sys: &Path, device: &str, depth: usize) -> Vec<String> {
    if depth >= MAX_STACK_DEPTH {
        return Vec::new();
    }
    let Ok(slaves) = std::fs::read_dir(sys.join("dev/block").join(device).join("slaves")) else {
        return Vec::new();
    };
    let mut disks = Vec::new();
    for slave in slaves.flatten() {
        let entry = sys.join("class/block").join(slave.file_name());
        let Some(number) = read_dev(&entry) else {
            continue;
        };
        let deeper = disks_under(sys, &number, depth + 1);
        if !deeper.is_empty() {
            disks.extend(deeper);
            continue;
        }
        // A partition stands for its disk: io.stat accounts whole disks.
        let whole = if entry.join("partition").exists() {
            read_dev(&entry.join("..")).unwrap_or(number)
        } else {
            number
        };
        if !disks.contains(&whole) {
            disks.push(whole);
        }
    }
    disks
}

fn read_dev(entry: &Path) -> Option<String> {
    let text = std::fs::read_to_string(entry.join("dev")).ok()?;
    let number = text.trim();
    (!number.is_empty()).then(|| number.to_string())
}

/// Bytes the container has written to block devices since it started; `None` when the stats carry
/// no write counter: a stopped container (`null`), one that has done no block I/O yet, or a host
/// that does not account block I/O per container — Docker Desktop answers `[]` for every
/// container (checked 2026-09-29, Engine 29.6.2 on WSL2), so no sensor registers there. A stacked
/// device whose disk is listed as well is not counted: the disk already carries that write.
pub fn written_bytes(stats: &ContainerStats, stacking: &mut Stacking) -> Option<u64> {
    let entries = stats.blkio_stats.io_service_bytes_recursive.as_ref()?;
    let writes: Vec<(String, u64)> = entries
        .iter()
        .filter(|entry| entry.op.eq_ignore_ascii_case("write"))
        .map(|entry| (format!("{}:{}", entry.major, entry.minor), entry.value))
        .collect();
    if writes.is_empty() {
        return None;
    }
    let listed: HashSet<&str> = writes.iter().map(|(device, _)| device.as_str()).collect();
    let mut total = 0u64;
    for (device, value) in &writes {
        let counted_below = stacking
            .under(device)
            .iter()
            .any(|disk| listed.contains(disk.as_str()));
        if !counted_below {
            total = total.saturating_add(*value);
        }
    }
    Some(total)
}

/// A container's counter at its last sample.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Baseline {
    pub bytes: u64,
    /// Unix milliseconds of the sample.
    pub at_ms: i64,
    /// The container's `State.StartedAt` then; a different one means the counter restarted.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub started_at: Option<String>,
    /// A start time known to be stale: the counter already dropped (a restart the state poll has
    /// not reported yet) while this was still the reported start time. Reported again, it is
    /// treated as unknown; the next different one is adopted without a second skip.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub ignored_started_at: Option<String>,
    /// When the start time began to be ignored (Unix ms).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub ignored_since_ms: Option<i64>,
}

/// Why a sample added nothing.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Skip {
    /// No earlier sample of this container (first sight, or a new container id).
    FirstSample,
    /// The counter went backwards.
    CounterReset,
    /// The container was restarted (a new `State.StartedAt`): its counter began again from 0.
    Restarted,
    /// The wall clock went backwards.
    ClockBackwards,
    /// The probe missed samples across an hour boundary: the bytes cannot be placed in an hour.
    GapAcrossHours,
    /// The clock passed the running hour's end before the tick rolled it: nothing is counted and
    /// the baseline is kept, so the next sample credits these bytes to the new hour.
    HourNotRolled,
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
    /// Accepted deltas in the hour; 0 ⇒ nothing to post.
    pub deltas: u32,
    /// Each current container's last counter.
    #[serde(default)]
    pub baselines: BTreeMap<String, Baseline>,
    /// Unix seconds of the last hour [`WriteRecord::roll`] handed out to post: never reopened by a
    /// clock stepped back into it, never handed out again (#1489).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub posted_hour: Option<i64>,
}

impl WriteRecord {
    pub fn new(now_ms: i64) -> Self {
        Self {
            hour_start: hour_start(now_ms),
            ..Self::default()
        }
    }

    /// Close the running hour when `now_ms` is past it and start the current one (baselines kept).
    /// Returns the closed hour when it has something honest to post and was not handed out before.
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
        let postable = self.deltas > 0 && self.posted_hour != Some(done.hour_start);
        self.hour_start = current;
        self.bytes = 0;
        self.covered_ms = 0;
        self.deltas = 0;
        if postable {
            self.posted_hour = Some(done.hour_start);
        }
        postable.then_some(done)
    }

    /// Record the container's counter read at `now_ms` and add its delta to the running hour at
    /// once — the baseline and the hour move together, so a round cut short (a stop, the daemon
    /// gone) loses nothing. Returns the delta and the interval it covers (ms), or why there is
    /// none. The new counter always becomes the baseline.
    pub fn sample(
        &mut self,
        container_id: &str,
        written: u64,
        now_ms: i64,
        sample_period: Duration,
        started_at: Option<&str>,
    ) -> Result<(u64, u64), Skip> {
        // The wall clock went back past the running hour (a VM restored, a clock corrected):
        // what was counted belongs to an hour that has not come yet — start the current one
        // afresh rather than let hours of writes pile into it. The one exception is a *small* step
        // back into the hour just posted (an NTP step of seconds across the boundary): the running
        // hour is kept, since reopening the posted hour would only collect a few seconds' bytes
        // that `roll` then refuses to hand out twice (#1489). "Small" is the longest interval a
        // sample may cover (`MAX_INTERVAL_FACTOR` sample periods, 15 s at the 5 s default): a step
        // within it is indistinguishable from one late sample; a larger one is a real correction
        // and resets as any other step back does. Either way `roll` never posts the posted hour
        // again.
        let now_hour = hour_start(now_ms);
        if now_hour < self.hour_start {
            let tolerance_ms = sample_period.as_secs_f64() * 1000.0 * contract::MAX_INTERVAL_FACTOR;
            let step_ms = self.hour_start.saturating_mul(1000).saturating_sub(now_ms);
            let small_step_into_posted =
                self.posted_hour == Some(now_hour) && step_ms as f64 <= tolerance_ms;
            if !small_step_into_posted {
                self.hour_start = now_hour;
                self.bytes = 0;
                self.covered_ms = 0;
                self.deltas = 0;
            }
        }
        // A sample read after the boundary, in a tick that rolled before it: leave everything for
        // the next tick, which rolls first — no write of the new hour goes into the old one.
        if hour_start(now_ms) > self.hour_start {
            return Err(Skip::HourNotRolled);
        }
        let (ignored, ignored_since) = self
            .baselines
            .get(container_id)
            .map(|baseline| {
                (
                    baseline.ignored_started_at.clone(),
                    baseline.ignored_since_ms,
                )
            })
            .unwrap_or_default();
        // The stale start time stays ignored until the poll reports a different one — or, if the
        // poll keeps reporting it for three polls, the drop was no restart and it is trusted again.
        let expired = ignored_since.is_some_and(|since| now_ms - since > STALE_START_GRACE_MS);
        let still_ignored = ignored.is_some()
            && !expired
            && started_at.is_none_or(|now| Some(now) == ignored.as_deref());
        let started_at = if still_ignored { None } else { started_at };
        let current = Baseline {
            bytes: written,
            at_ms: now_ms,
            started_at: started_at.map(str::to_string),
            ignored_started_at: if still_ignored { ignored } else { None },
            ignored_since_ms: if still_ignored { ignored_since } else { None },
        };
        let previous = self
            .baselines
            .insert(container_id.to_string(), current)
            .ok_or(Skip::FirstSample)?;
        if let (Some(before), Some(now)) = (previous.started_at.as_deref(), started_at) {
            if before != now {
                return Err(Skip::Restarted);
            }
        }
        if written < previous.bytes {
            // Most likely a restart the state poll has not reported yet: the start time reported
            // now is stale. The new baseline claims none and ignores that value until the poll
            // reports the new one, which is then adopted without a second skip.
            if let Some(baseline) = self.baselines.get_mut(container_id) {
                if let Some(stale) = baseline.started_at.take() {
                    baseline.ignored_started_at = Some(stale);
                    baseline.ignored_since_ms = Some(now_ms);
                }
            }
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
        let delta = written - previous.bytes;
        self.bytes = self.bytes.saturating_add(delta);
        self.deltas = self.deltas.saturating_add(1);
        Ok((delta, gap))
    }

    /// Count one sample round of the service as measured time: `gap_ms` is the longest interval
    /// among its replicas' accepted deltas. Coverage never exceeds the part of the hour that
    /// passed; it only feeds the comment.
    pub fn cover(&mut self, gap_ms: u64, now_ms: i64) {
        let into_hour = u64::try_from(now_ms - self.hour_start * 1000).unwrap_or(0);
        self.covered_ms = self
            .covered_ms
            .saturating_add(gap_ms.min(into_hour))
            .min(HOUR_MS as u64);
    }

    /// A running container that has done no block I/O yet (its stats list no device) on a host
    /// that does account I/O: its counter is 0 since it started, so that is its baseline — its
    /// first write then counts in full instead of becoming the baseline. A real counter of the
    /// same start is kept; one of an earlier start (restarted, not written since) is replaced.
    pub fn zero_baseline(&mut self, container_id: &str, now_ms: i64, started_at: &str) {
        let keep = self.baselines.get(container_id).is_some_and(|baseline| {
            let same_start = baseline
                .started_at
                .as_deref()
                .is_none_or(|s| s == started_at)
                || baseline.ignored_started_at.as_deref() == Some(started_at);
            baseline.bytes > 0 && same_start
        });
        if !keep {
            self.baselines.insert(
                container_id.to_string(),
                Baseline {
                    bytes: 0,
                    at_ms: now_ms,
                    started_at: Some(started_at.to_string()),
                    ..Baseline::default()
                },
            );
        }
    }

    /// Forget one container's baseline (its counter is gone); whether there was one.
    pub fn forget(&mut self, container_id: &str) -> bool {
        self.baselines.remove(container_id).is_some()
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
        let mut plain = Stacking::default();
        assert_eq!(
            written_bytes(by_prefix(&t0, "abbd59dcacc8"), &mut plain),
            Some(11_377_098_752)
        );
        assert_eq!(
            written_bytes(by_prefix(&t1, "abbd59dcacc8"), &mut plain),
            Some(11_377_262_592)
        );
        // The exited ci-image has `io_service_bytes_recursive: null`: no counter, not 0.
        assert_eq!(
            written_bytes(by_prefix(&t0, "3537ef2ee867"), &mut plain),
            None
        );
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
        let mut plain = Stacking::default();
        assert_eq!(written_bytes(&stats, &mut plain), Some(150));
        let empty: ContainerStats =
            serde_json::from_value(serde_json::json!({ "blkio_stats": {} })).unwrap();
        assert_eq!(written_bytes(&empty, &mut plain), None);
        // Docker Desktop's shape: every list present and empty.
        let desktop: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [], "sectors_recursive": [] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&desktop, &mut plain), None);
        let reads_only: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 8, "minor": 0, "op": "read", "value": 7 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&reads_only, &mut plain), None);
    }

    /// A root on LVM over `sda2`: `dm-0` (253:0) sits on the partition, the partition on `sda`.
    #[cfg(unix)]
    fn lvm_sysfs() -> crate::probe_only::host::tests::FakeTree {
        let tree = crate::probe_only::host::tests::FakeTree::new("stacking");
        tree.file("sys/devices/virtual/block/dm-0/dev", "253:0\n");
        tree.file("sys/devices/block/sda/dev", "8:0\n");
        tree.file("sys/devices/block/sda/sda2/dev", "8:2\n");
        tree.file("sys/devices/block/sda/sda2/partition", "2\n");
        tree.link("sys/dev/block/253:0", "../../devices/virtual/block/dm-0");
        tree.link("sys/dev/block/8:0", "../../devices/block/sda");
        tree.link("sys/class/block/sda2", "../../devices/block/sda/sda2");
        tree.link("sys/class/block/dm-0", "../../devices/virtual/block/dm-0");
        tree.link(
            "sys/devices/virtual/block/dm-0/slaves/sda2",
            "../../../../block/sda/sda2",
        );
        tree
    }

    #[test]
    fn a_restart_of_the_same_container_is_a_new_baseline_even_above_the_old_counter() {
        let mut record = WriteRecord::new(H13);
        let first = Some("2026-09-29T08:00:00Z");
        record.sample("a", 1_000, H13, PERIOD, first).ok();
        assert_eq!(
            record.sample("a", 1_500, H13 + 5_000, PERIOD, first),
            Ok((500, 5_000))
        );
        // Restarted while the probe was down: same id, a new cgroup that has already written more
        // than the old counter. Not a delta of 300 — a baseline.
        let second = Some("2026-09-29T13:20:00Z");
        assert_eq!(
            record.sample("a", 1_800, H13 + 30 * MIN, PERIOD, second),
            Err(Skip::Restarted)
        );
        assert_eq!(
            record.sample("a", 1_900, H13 + 30 * MIN + 5_000, PERIOD, second),
            Ok((100, 5_000))
        );
        assert_eq!(record.bytes, 600);
        // Restarted while the probe runs: the counter drop is seen first (a reset); the samples
        // until the next state poll still carry the old start time, and the poll then reports the
        // new one. One restart, one skipped sample — no `Restarted` (and so no log line) later.
        let third = Some("2026-09-29T13:40:00Z");
        let before = record.bytes;
        let t = H13 + 40 * MIN;
        assert_eq!(
            record.sample("a", 50, t, PERIOD, second),
            Err(Skip::CounterReset)
        );
        for (step, counter, started) in [
            (1, 60, second),
            (2, 70, second),
            (3, 80, third),
            (4, 90, third),
        ] {
            assert_eq!(
                record.sample("a", counter, t + step * 5_000, PERIOD, started),
                Ok((10, 5_000)),
                "sample {step}"
            );
        }
        assert_eq!(record.bytes, before + 40, "only the reset sample is lost");
        assert_eq!(record.baselines["a"].started_at.as_deref(), third);
        assert_eq!(record.baselines["a"].ignored_started_at, None);

        // A drop that was no restart (a device left the counters): the poll keeps reporting the
        // same start time. After three polls it is trusted again, so a later restart is caught.
        let t = H13 + 45 * MIN;
        assert_eq!(
            record.sample("a", 5, t, PERIOD, third),
            Err(Skip::CounterReset)
        );
        record.sample("a", 6, t + 60_000, PERIOD, third).unwrap();
        assert_eq!(record.baselines["a"].started_at, None, "still ignored");
        record.sample("a", 7, t + 181_000, PERIOD, third).unwrap();
        assert_eq!(
            record.baselines["a"].started_at.as_deref(),
            third,
            "trusted again"
        );
        let fourth = Some("2026-09-29T13:50:00Z");
        assert_eq!(
            record.sample("a", 9_000, t + 186_000, PERIOD, fourth),
            Err(Skip::Restarted)
        );
        // A baseline from before the start time was known still pairs.
        record.sample("b", 10, H13, PERIOD, None).ok();
        assert!(record.sample("b", 20, H13 + 5_000, PERIOD, first).is_ok());
    }

    #[cfg(unix)]
    #[test]
    fn a_write_through_lvm_counts_once_on_the_disk() {
        let tree = lvm_sysfs();
        let mut stacking = Stacking::new(Some(tree.0.join("sys")));
        assert_eq!(stacking.under("253:0"), ["8:0".to_string()]);
        assert!(stacking.under("8:0").is_empty());
        // The kernel accounts the container's write on dm-0 and again on sda.
        let both: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 253, "minor": 0, "op": "write", "value": 4096 },
                { "major": 8, "minor": 0, "op": "write", "value": 4096 },
                { "major": 8, "minor": 16, "op": "write", "value": 100 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&both, &mut stacking), Some(4096 + 100));
        // A kernel that lists only the stacked device: it is all there is, so it counts.
        let top_only: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 253, "minor": 0, "op": "write", "value": 4096 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&top_only, &mut stacking), Some(4096));
    }

    /// md RAID1 over `sda1` and `sdb1`: `md0` (9:0) has both partitions as slaves.
    #[cfg(unix)]
    #[test]
    fn a_mirrored_write_counts_once_per_member_disk() {
        let tree = crate::probe_only::host::tests::FakeTree::new("raid1");
        tree.file("sys/devices/virtual/block/md0/dev", "9:0\n");
        for (disk, number, partition, partition_number) in [
            ("sda", "8:0", "sda1", "8:1"),
            ("sdb", "8:16", "sdb1", "8:17"),
        ] {
            tree.file(
                &format!("sys/devices/block/{disk}/dev"),
                &format!("{number}\n"),
            );
            tree.file(
                &format!("sys/devices/block/{disk}/{partition}/dev"),
                &format!("{partition_number}\n"),
            );
            tree.file(
                &format!("sys/devices/block/{disk}/{partition}/partition"),
                "1\n",
            );
            tree.link(
                &format!("sys/class/block/{partition}"),
                &format!("../../devices/block/{disk}/{partition}"),
            );
            tree.link(
                &format!("sys/devices/virtual/block/md0/slaves/{partition}"),
                &format!("../../../../block/{disk}/{partition}"),
            );
        }
        tree.link("sys/dev/block/9:0", "../../devices/virtual/block/md0");
        let mut stacking = Stacking::new(Some(tree.0.join("sys")));
        assert_eq!(
            stacking.under("9:0"),
            ["8:0".to_string(), "8:16".to_string()]
        );
        // One 4 KiB logical write lands on both mirrors: the physical wear is 8 KiB (intended —
        // the sensor answers "what wears the disks"; the description says so).
        let mirrored: ContainerStats = serde_json::from_value(serde_json::json!({
            "blkio_stats": { "io_service_bytes_recursive": [
                { "major": 9, "minor": 0, "op": "write", "value": 4096 },
                { "major": 8, "minor": 0, "op": "write", "value": 4096 },
                { "major": 8, "minor": 16, "op": "write", "value": 4096 }
            ] }
        }))
        .unwrap();
        assert_eq!(written_bytes(&mirrored, &mut stacking), Some(8192));
    }

    #[test]
    fn deltas_accumulate_into_the_hour_and_post_after_it() {
        let mut record = WriteRecord::new(H13);
        assert_eq!(
            record.sample("a", 1_000, H13 + 10_000, PERIOD, None),
            Err(Skip::FirstSample)
        );
        // A delta goes into the hour as it is read (a round cut short loses nothing).
        record
            .sample("a", 1_000, H13 + 10_000, PERIOD, None)
            .unwrap();
        assert_eq!((record.bytes, record.deltas), (0, 1));
        let mut at = H13 + 10_000;
        let mut counter = 1_000;
        // 2 MB every 5 s for the rest of the hour.
        while at + 5_000 < H13 + 60 * MIN {
            at += 5_000;
            counter += 2_000_000;
            let (_, gap) = record.sample("a", counter, at, PERIOD, None).unwrap();
            record.cover(gap, at);
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
            .sample("a", counter + 5, H13 + 60 * MIN + 2_000, PERIOD, None)
            .unwrap();
        assert_eq!(delta, 5);
    }

    #[test]
    fn an_hour_without_an_accepted_delta_is_not_posted_as_zero() {
        let mut record = WriteRecord::new(H13);
        record.sample("a", 1_000, H13 + 59 * MIN, PERIOD, None).ok();
        assert_eq!(record.roll(H13 + 61 * MIN), None);
        assert_eq!(
            record.sample("a", 1_000, H13 + 61 * MIN, PERIOD, None),
            Err(Skip::GapAcrossHours)
        );
        // …but an hour of measured silence is a real 0.
        let at = H13 + 61 * MIN + 5_000;
        let (_, gap) = record.sample("a", 1_000, at, PERIOD, None).unwrap();
        record.cover(gap, at);
        let done = record.roll(H13 + 121 * MIN).unwrap();
        assert_eq!(done.megabytes(), 0.0);
    }

    #[test]
    fn a_counter_reset_or_a_new_container_id_loses_only_its_sample() {
        let mut record = WriteRecord::new(H13);
        record.sample("old", 5_000, H13, PERIOD, None).ok();
        let (_, gap) = record
            .sample("old", 6_000, H13 + 5_000, PERIOD, None)
            .unwrap();
        record.cover(gap, H13 + 5_000);
        // Recreated: a new id with a fresh counter far below the old one — a baseline, not a
        // negative or a huge delta.
        assert_eq!(
            record.sample("new", 10, H13 + 10_000, PERIOD, None),
            Err(Skip::FirstSample)
        );
        record.retain(&["new".to_string()]);
        assert_eq!(record.baselines.len(), 1);
        let (_, gap) = record
            .sample("new", 110, H13 + 15_000, PERIOD, None)
            .unwrap();
        record.cover(gap, H13 + 15_000);
        // Same id, counter backwards: a baseline again.
        assert_eq!(
            record.sample("new", 50, H13 + 20_000, PERIOD, None),
            Err(Skip::CounterReset)
        );
        let (_, gap) = record
            .sample("new", 80, H13 + 25_000, PERIOD, None)
            .unwrap();
        record.cover(gap, H13 + 25_000);
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
        record.sample("a", 0, H13 + MIN, PERIOD, None).ok();
        // Ten minutes without a sample (the daemon did not answer), same hour: all of it counts.
        let (delta, gap) = record
            .sample("a", 700, H13 + 11 * MIN, PERIOD, None)
            .unwrap();
        assert_eq!((delta, gap), (700, 10 * MIN as u64));
        record.cover(gap, H13 + 11 * MIN);
        record
            .sample("a", 800, H13 + 55 * MIN, PERIOD, None)
            .unwrap();
        // Probe down from 13:55 to 14:05: the bytes belong to either hour — dropped.
        record.roll(H13 + 65 * MIN);
        assert_eq!(
            record.sample("a", 900, H13 + 65 * MIN, PERIOD, None),
            Err(Skip::GapAcrossHours)
        );
        // An ordinary sample across the boundary is not a gap.
        let mut record = WriteRecord::new(H13);
        record
            .sample("a", 0, H13 + 60 * MIN - 2_000, PERIOD, None)
            .ok();
        record.roll(H13 + 60 * MIN + 3_000);
        assert_eq!(
            record.sample("a", 10, H13 + 60 * MIN + 3_000, PERIOD, None),
            Ok((10, 5_000))
        );
        // The clock going backwards is a new baseline.
        assert_eq!(
            record.sample("a", 20, H13 + 50 * MIN, PERIOD, None),
            Err(Skip::ClockBackwards)
        );
    }

    #[test]
    fn an_idle_containers_first_write_counts_in_full() {
        let mut record = WriteRecord::new(H13);
        let started = "2026-09-29T12:00:00Z";
        // Running, no device in io.stat yet: a zero baseline, refreshed while it stays idle.
        record.zero_baseline("a", H13 + 5_000, started);
        record.zero_baseline("a", H13 + 10_000, started);
        // Its first burst: 1 GB, all of it counted.
        assert_eq!(
            record.sample("a", 1_000_000_000, H13 + 15_000, PERIOD, Some(started)),
            Ok((1_000_000_000, 5_000))
        );
        // A real counter of the same start is never replaced by a zero.
        record.zero_baseline("a", H13 + 20_000, started);
        assert_eq!(record.baselines["a"].bytes, 1_000_000_000);
        // Restarted and idle since: its counter is 0 again, and its first write counts too.
        let again = "2026-09-29T13:30:00Z";
        record.zero_baseline("a", H13 + 31 * MIN, again);
        assert_eq!(
            record.sample("a", 2_048, H13 + 31 * MIN + 5_000, PERIOD, Some(again)),
            Ok((2_048, 5_000))
        );
    }

    #[test]
    fn a_sample_after_the_boundary_waits_for_the_roll() {
        let mut record = WriteRecord::new(H13);
        record
            .sample("a", 0, H13 + 60 * MIN - 3_000, PERIOD, None)
            .ok();
        // 14:00:02, but the tick rolled at 13:59:59.9.
        assert_eq!(
            record.sample("a", 700, H13 + 60 * MIN + 2_000, PERIOD, None),
            Err(Skip::HourNotRolled)
        );
        assert_eq!(
            (record.bytes, record.deltas),
            (0, 0),
            "not credited to 13:00"
        );
        assert_eq!(record.baselines["a"].bytes, 0, "baseline kept");
        // The next tick rolls first; its sample credits the bytes to 14:00.
        assert_eq!(record.roll(H13 + 60 * MIN + 7_000), None);
        assert_eq!(
            record.sample("a", 800, H13 + 60 * MIN + 7_000, PERIOD, None),
            Ok((800, 10_000))
        );
        assert_eq!(record.hour_start * 1000, H13 + 60 * MIN);
    }

    #[test]
    fn a_clock_set_back_past_the_hour_starts_the_current_hour_afresh() {
        // The running hour is 16:00 (the clock ran three hours fast); it is corrected to 13:10.
        let mut record = WriteRecord::new(H13 + 3 * 60 * MIN);
        record.sample("a", 0, H13 + 3 * 60 * MIN, PERIOD, None).ok();
        record
            .sample("a", 500, H13 + 3 * 60 * MIN + 5_000, PERIOD, None)
            .unwrap();
        assert_eq!(
            record.sample("a", 600, H13 + 10 * MIN, PERIOD, None),
            Err(Skip::ClockBackwards)
        );
        assert_eq!(record.hour_start * 1000, H13, "the hour follows the clock");
        assert_eq!((record.bytes, record.deltas), (0, 0));
        record
            .sample("a", 700, H13 + 10 * MIN + 5_000, PERIOD, None)
            .unwrap();
        // 14:00: exactly the one hour measured is posted, not four hours in one.
        let done = record.roll(H13 + 60 * MIN + 1_000).unwrap();
        assert_eq!(done.bytes, 100);
    }

    #[test]
    fn a_small_clock_step_back_across_the_boundary_does_not_post_the_hour_again() {
        let mut record = WriteRecord::new(H13);
        record
            .sample("a", 0, H13 + 60 * MIN - 10_000, PERIOD, None)
            .ok();
        record
            .sample("a", 100, H13 + 60 * MIN - 5_000, PERIOD, None)
            .unwrap();
        // 14:00:01: 13:00–14:00 is posted, and the first delta of 14:00 is credited.
        let posted = H13 + 60 * MIN + 1_000;
        assert_eq!(record.roll(posted).expect("posted").bytes, 100);
        assert_eq!(
            record.sample("a", 150, posted, PERIOD, None),
            Ok((50, 6_000))
        );
        // An NTP step sets the clock back 3 s, to 13:59:58: the posted hour is not reopened and
        // what 14:00 already holds stays.
        let back = posted - 3_000;
        assert_eq!(record.roll(back), None);
        assert_eq!(
            record.sample("a", 170, back, PERIOD, None),
            Err(Skip::ClockBackwards)
        );
        assert_eq!(record.hour_start * 1000, H13 + 60 * MIN);
        assert_eq!((record.bytes, record.deltas), (50, 1));
        // 14:00:02 by the stepped clock: no second, tiny 13:00–14:00 value; the writes since the
        // step count towards 14:00.
        let after = back + 4_000;
        assert_eq!(record.roll(after), None, "13:00–14:00 is not posted twice");
        assert_eq!(
            record.sample("a", 200, after, PERIOD, None),
            Ok((30, 4_000))
        );
        let next = record.roll(H13 + 120 * MIN + 1_000).expect("14:00 posted");
        assert_eq!((next.hour_start * 1000, next.bytes), (H13 + 60 * MIN, 80));
        assert_eq!(record.posted_hour, Some(next.hour_start));

        // However an hour gets reopened, the one just handed out is never handed out again.
        let mut reopened = WriteRecord {
            hour_start: H13 / 1000,
            deltas: 1,
            posted_hour: Some(H13 / 1000),
            ..WriteRecord::default()
        };
        assert_eq!(reopened.roll(H13 + 60 * MIN), None);
        // A record saved before 0.7.0 has no posted hour: it posts as before.
        let old: WriteRecord = serde_json::from_str(
            r#"{"hourStart": 1790686800, "bytes": 5, "coveredMs": 0, "deltas": 1}"#,
        )
        .unwrap();
        assert_eq!(old.posted_hour, None);
    }

    /// A large step back into the posted hour (a VM restore, a bad RTC corrected by chrony) is a
    /// real correction: the running hour resets as before #1489 instead of collecting ~2 h of
    /// writes, and the posted hour is still never handed out again.
    #[test]
    fn a_large_clock_step_back_into_the_posted_hour_resets_the_running_hour() {
        let mut record = WriteRecord::new(H13);
        record
            .sample("a", 0, H13 + 60 * MIN - 10_000, PERIOD, None)
            .ok();
        record
            .sample("a", 100, H13 + 60 * MIN - 5_000, PERIOD, None)
            .unwrap();
        let posted = H13 + 60 * MIN + 1_000;
        assert_eq!(record.roll(posted).expect("posted").bytes, 100);
        assert_eq!(
            record.sample("a", 150, posted, PERIOD, None),
            Ok((50, 6_000))
        );
        // The clock is corrected back 50 minutes, to 13:10:01.
        let back = posted - 50 * MIN;
        assert_eq!(record.roll(back), None);
        assert_eq!(
            record.sample("a", 170, back, PERIOD, None),
            Err(Skip::ClockBackwards)
        );
        assert_eq!(
            record.hour_start * 1000,
            H13,
            "the running hour restarts at 13:00"
        );
        assert_eq!(
            (record.bytes, record.deltas),
            (0, 0),
            "14:00's bytes are dropped"
        );
        // The writes of the stepped 13:10–14:00 go to the reopened 13:00 hour …
        let mut at = back;
        let mut written = 170;
        while at + 5_000 < H13 + 60 * MIN {
            at += 5_000;
            written += 1;
            record.sample("a", written, at, PERIOD, None).unwrap();
        }
        assert!(record.deltas > 0);
        // … which is not posted a second time when the stepped clock reaches 14:00.
        assert_eq!(
            record.roll(H13 + 60 * MIN + 1_000),
            None,
            "13:00–14:00 is not posted twice"
        );
        assert_eq!(record.hour_start * 1000, H13 + 60 * MIN);
        assert_eq!((record.bytes, record.deltas), (0, 0));
        // 14:00 then collects only its own hour and posts once, covering at most an hour.
        let mut at = H13 + 60 * MIN + 1_000;
        record.sample("a", written + 5, at, PERIOD, None).unwrap();
        while at + 5_000 < H13 + 120 * MIN {
            at += 5_000;
            written += 1;
            let (_, gap) = record.sample("a", written + 5, at, PERIOD, None).unwrap();
            record.cover(gap, at);
        }
        let next = record.roll(H13 + 120 * MIN + 1_000).expect("14:00 posted");
        assert_eq!(next.hour_start * 1000, H13 + 60 * MIN);
        assert!(next.covered_ms <= 3_600_000, "{}", next.covered_ms);

        // Just outside the tolerance (3 sample periods = 15 s) already resets; just inside keeps.
        for (step_ms, kept) in [(15_000, true), (16_000, false)] {
            let mut record = WriteRecord::new(H13);
            record
                .sample("a", 0, H13 + 60 * MIN - 5_000, PERIOD, None)
                .ok();
            record
                .sample("a", 10, H13 + 60 * MIN - 1_000, PERIOD, None)
                .unwrap();
            record.roll(H13 + 60 * MIN).expect("posted");
            record
                .sample("a", 20, H13 + 60 * MIN + 1_000, PERIOD, None)
                .unwrap();
            record
                .sample("a", 30, H13 + 60 * MIN - step_ms, PERIOD, None)
                .ok();
            assert_eq!(
                record.hour_start * 1000 == H13 + 60 * MIN,
                kept,
                "{step_ms} ms"
            );
        }
    }

    #[test]
    fn a_partial_hour_says_how_much_of_it_was_measured() {
        // The probe started at 13:40.
        let mut record = WriteRecord::new(H13 + 40 * MIN);
        record.sample("a", 0, H13 + 40 * MIN, PERIOD, None).ok();
        let mut at = H13 + 40 * MIN;
        while at + 5_000 < H13 + 60 * MIN {
            at += 5_000;
            let (_, gap) = record.sample("a", 1, at, PERIOD, None).unwrap();
            record.cover(gap, at);
        }
        let done = record.roll(H13 + 60 * MIN + 1_000).unwrap();
        assert_eq!(done.comment(), "13:00–14:00 UTC; measured 19 of 60 min");
        // The first round of an hour covers only the part since the boundary.
        let mut record = WriteRecord::new(H13 + 60 * MIN);
        record.cover(5_000, H13 + 60 * MIN + 3_000);
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
        record.sample("abc", 42, H13 + 1_000, PERIOD, None).ok();
        record.cover(5_000, H13 + 1_000);
        let text = serde_json::to_string(&record).unwrap();
        assert!(text.contains("\"hourStart\""), "{text}");
        let back: WriteRecord = serde_json::from_str(&text).unwrap();
        assert_eq!(back, record);
    }
}
