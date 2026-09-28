//! CPU and memory math over one-shot Engine API stats.
//!
//! CPU is a **percentage of the whole host** (all cores = 100 %): Δ`cpu_stats.cpu_usage.total_usage`
//! / Δ`cpu_stats.system_cpu_usage` × 100 between two of the probe's own samples. It is deliberately
//! NOT multiplied by `online_cpus` — `docker stats` does that and shows per-core percent (up to
//! 400 % on four cores); the sensor description says so. A sample that cannot give an honest delta
//! is skipped, never posted as 0: the first sample of a container, a counter that went backwards,
//! and an interval outside ½…3× the sample period (a gap after an outage must not become one
//! averaged sample).
//!
//! Memory used is `usage − inactive_file` (the `docker stats` convention on cgroup v2; cgroup v1's
//! `total_inactive_file` is honored too) over the limit. A container without a limit reports the
//! host's memory as its limit; such a container is measured against the host's `MemTotal`.

use std::collections::HashMap;
use std::time::Duration;

use super::contract;
use super::engine::ContainerStats;

/// One CPU observation: the two cumulative counters and when (monotonic) they were read.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct CpuCounters {
    pub container_ns: u64,
    pub system_ns: u64,
    pub at: Duration,
}

impl CpuCounters {
    pub fn from_stats(stats: &ContainerStats, at: Duration) -> Option<Self> {
        Some(Self {
            container_ns: stats.cpu_stats.cpu_usage.total_usage?,
            system_ns: stats.cpu_stats.system_cpu_usage?,
            at,
        })
    }
}

/// Why a CPU sample produced no value.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum CpuSkip {
    /// No earlier sample of this container (first sight, or a new container id).
    FirstSample,
    /// A counter went backwards or the host counter did not advance.
    CounterReset,
    /// The two samples are too close together or too far apart.
    AbnormalInterval,
}

/// `Ok(percent of the host)` or why the sample was skipped.
pub fn cpu_percent(
    previous: &CpuCounters,
    current: &CpuCounters,
    sample_period: Duration,
) -> Result<f64, CpuSkip> {
    if current.container_ns < previous.container_ns || current.system_ns <= previous.system_ns {
        return Err(CpuSkip::CounterReset);
    }
    let interval = current.at.saturating_sub(previous.at).as_secs_f64();
    let period = sample_period.as_secs_f64();
    if interval < period * contract::MIN_INTERVAL_FACTOR
        || interval > period * contract::MAX_INTERVAL_FACTOR
    {
        return Err(CpuSkip::AbnormalInterval);
    }
    let container = (current.container_ns - previous.container_ns) as f64;
    let system = (current.system_ns - previous.system_ns) as f64;
    Ok((container / system * 100.0).clamp(0.0, 100.0))
}

/// The last CPU counters of every container, keyed by container id. A new id (recreate) has no
/// entry, so its first sample is skipped: that is the "container-id change" rule.
#[derive(Debug, Default)]
pub struct CpuTracker {
    last: HashMap<String, CpuCounters>,
}

impl CpuTracker {
    /// Record `current` for `container_id` and return the percent since the previous sample. The
    /// new counters always become the baseline, so one skipped sample never skips the next.
    pub fn sample(
        &mut self,
        container_id: &str,
        current: CpuCounters,
        sample_period: Duration,
    ) -> Result<f64, CpuSkip> {
        let outcome = match self.last.get(container_id) {
            None => Err(CpuSkip::FirstSample),
            Some(previous) => cpu_percent(previous, &current, sample_period),
        };
        self.last.insert(container_id.to_string(), current);
        outcome
    }

    /// Forget a container whose sample failed, so a stale baseline is never paired with a later
    /// one across the failure.
    pub fn forget(&mut self, container_id: &str) {
        self.last.remove(container_id);
    }

    /// Drop the baselines of containers that are no longer sampled.
    pub fn retain(&mut self, mut keep: impl FnMut(&str) -> bool) {
        self.last.retain(|id, _| keep(id));
    }

    #[cfg(test)]
    pub fn len(&self) -> usize {
        self.last.len()
    }
}

/// One container's memory reading.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MemoryReading {
    /// `usage − inactive_file`, bytes.
    pub used: u64,
    /// The limit the percentage is taken against, bytes: the container limit, or the host's
    /// `MemTotal` when the container has none.
    pub limit: u64,
    /// The container has no memory limit of its own.
    pub unlimited: bool,
}

/// Read memory from a stats response. `None` when the container reports no memory counters (it is
/// not running) or a zero limit.
pub fn memory_reading(
    stats: &ContainerStats,
    host_mem_total: Option<u64>,
) -> Option<MemoryReading> {
    let memory = &stats.memory_stats;
    let usage = memory.usage?;
    let limit = memory.limit.filter(|limit| *limit > 0)?;
    let inactive_file = memory.stats.as_ref().and_then(|stats| {
        stats
            .get("inactive_file")
            .or_else(|| stats.get("total_inactive_file"))
            .copied()
    });
    // Same guard as the docker CLI: subtract only when it leaves something.
    let used = match inactive_file {
        Some(inactive) if inactive < usage => usage - inactive,
        _ => usage,
    };
    let (limit, unlimited) = match host_mem_total {
        Some(total) if total > 0 && limit >= total => (total, true),
        _ => (limit, false),
    };
    Some(MemoryReading {
        used,
        limit,
        unlimited,
    })
}

/// A service's memory: replicas summed; the summed limit is capped at the host's memory, since no
/// set of containers can use more than the host has.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ServiceMemory {
    pub used_percent: f64,
    pub limit_bytes: u64,
    pub unlimited: bool,
}

pub fn service_memory(
    readings: &[MemoryReading],
    host_mem_total: Option<u64>,
) -> Option<ServiceMemory> {
    if readings.is_empty() {
        return None;
    }
    let used: u64 = readings.iter().map(|r| r.used).sum();
    let mut limit: u64 = readings.iter().map(|r| r.limit).sum();
    if let Some(total) = host_mem_total.filter(|total| *total > 0) {
        limit = limit.min(total);
    }
    if limit == 0 {
        return None;
    }
    Some(ServiceMemory {
        used_percent: (used as f64 / limit as f64 * 100.0).max(0.0),
        limit_bytes: limit,
        unlimited: readings.iter().any(|r| r.unlimited),
    })
}

/// Megabytes (MiB) for the `Memory limit` Int sensor, saturating.
pub fn limit_megabytes(bytes: u64) -> i32 {
    i32::try_from(bytes / contract::BYTES_PER_MB).unwrap_or(i32::MAX)
}

/// `MemTotal` from `/proc/meminfo` text, in bytes.
pub fn parse_mem_total(meminfo: &str) -> Option<u64> {
    meminfo.lines().find_map(|line| {
        let rest = line.strip_prefix("MemTotal:")?;
        let mut parts = rest.split_whitespace();
        let value: u64 = parts.next()?.parse().ok()?;
        match parts.next() {
            Some("kB") => value.checked_mul(1024),
            None => Some(value),
            Some(_) => None,
        }
    })
}

#[cfg(test)]
mod tests {
    use super::super::engine::tests::{STATS_T0, STATS_T1};
    use super::*;

    const PERIOD: Duration = Duration::from_secs(5);
    const GARAGE_MEM_TOTAL: u64 = 16_298_872 * 1024;

    fn counters(container_ns: u64, system_ns: u64, at_ms: u64) -> CpuCounters {
        CpuCounters {
            container_ns,
            system_ns,
            at: Duration::from_millis(at_ms),
        }
    }

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
    fn one_busy_core_of_four_is_twenty_five_percent_of_the_host() {
        // system_cpu_usage advances by 4 cores × 5 s; the container used one core for 5 s.
        let previous = counters(0, 0, 0);
        let current = counters(5_000_000_000, 20_000_000_000, 5_000);
        assert_eq!(cpu_percent(&previous, &current, PERIOD), Ok(25.0));
        // Two busy cores: 50 % — `docker stats` would show 200 %.
        let current = counters(10_000_000_000, 20_000_000_000, 5_000);
        assert_eq!(cpu_percent(&previous, &current, PERIOD), Ok(50.0));
    }

    #[test]
    fn the_garage_gitea_db_delta_is_what_docker_stats_shows_divided_by_the_cores() {
        let t0 = garage_stats(STATS_T0);
        let t1 = garage_stats(STATS_T1);
        let a = CpuCounters::from_stats(by_prefix(&t0, "abbd59dcacc8"), Duration::ZERO).unwrap();
        let b =
            CpuCounters::from_stats(by_prefix(&t1, "abbd59dcacc8"), Duration::from_millis(5_272))
                .unwrap();
        let percent = cpu_percent(&a, &b, PERIOD).unwrap();
        // Δtotal 2_479_389_000 / Δsystem 20_780_000_000 = 11.93 % of the host; docker stats,
        // multiplying by online_cpus = 4, showed ~47.7 %.
        assert!((percent - 11.931).abs() < 0.01, "{percent}");
        let raw: serde_json::Value = serde_json::from_str(STATS_T1).unwrap();
        let online = raw
            .as_object()
            .unwrap()
            .iter()
            .find(|(id, _)| id.starts_with("abbd59dcacc8"))
            .unwrap()
            .1["cpu_stats"]["online_cpus"]
            .as_u64();
        assert_eq!(online, Some(4));
    }

    #[test]
    fn the_first_sample_of_a_container_is_skipped_not_zero() {
        let mut tracker = CpuTracker::default();
        assert_eq!(
            tracker.sample("a", counters(10, 100, 0), PERIOD),
            Err(CpuSkip::FirstSample)
        );
        assert_eq!(
            tracker.sample("a", counters(20, 200, 5_000), PERIOD),
            Ok(10.0)
        );
    }

    #[test]
    fn a_new_container_id_starts_a_new_baseline() {
        // Recreate: the service's new container has fresh counters far below the old ones. Without
        // per-id baselines that would read as a reset (or, worse, a huge delta).
        let mut tracker = CpuTracker::default();
        tracker
            .sample("old", counters(9_000, 100_000, 0), PERIOD)
            .ok();
        tracker
            .sample("old", counters(9_500, 105_000, 5_000), PERIOD)
            .ok();
        assert_eq!(
            tracker.sample("new", counters(10, 105_100, 5_100), PERIOD),
            Err(CpuSkip::FirstSample)
        );
        tracker.retain(|id| id == "new");
        assert_eq!(tracker.len(), 1);
    }

    #[test]
    fn a_counter_reset_is_skipped_and_rebaselines() {
        let mut tracker = CpuTracker::default();
        tracker.sample("a", counters(1_000, 10_000, 0), PERIOD).ok();
        assert_eq!(
            tracker.sample("a", counters(10, 20_000, 5_000), PERIOD),
            Err(CpuSkip::CounterReset)
        );
        // The reset sample is the new baseline: the next one is valid again.
        assert_eq!(
            tracker.sample("a", counters(1_010, 30_000, 10_000), PERIOD),
            Ok(10.0)
        );
        // A host counter that did not move cannot divide.
        tracker.sample("b", counters(1, 10, 0), PERIOD).ok();
        assert_eq!(
            tracker.sample("b", counters(2, 10, 5_000), PERIOD),
            Err(CpuSkip::CounterReset)
        );
    }

    #[test]
    fn abnormal_intervals_are_skipped() {
        let previous = counters(0, 0, 0);
        // Too soon (< 2.5 s for a 5 s period) and too late (> 15 s, e.g. after an outage).
        for at_ms in [2_000, 15_001, 600_000] {
            assert_eq!(
                cpu_percent(&previous, &counters(1, 100, at_ms), PERIOD),
                Err(CpuSkip::AbnormalInterval),
                "{at_ms}"
            );
        }
        // The edges themselves are accepted.
        for at_ms in [2_500, 15_000] {
            assert!(cpu_percent(&previous, &counters(1, 100, at_ms), PERIOD).is_ok());
        }
    }

    #[test]
    fn cpu_never_exceeds_the_whole_host() {
        let percent = cpu_percent(&counters(0, 0, 0), &counters(120, 100, 5_000), PERIOD);
        assert_eq!(percent, Ok(100.0));
    }

    #[test]
    fn a_stopped_container_has_no_counters() {
        let t0 = garage_stats(STATS_T0);
        let stopped = by_prefix(&t0, "3537ef2ee867");
        assert_eq!(CpuCounters::from_stats(stopped, Duration::ZERO), None);
        assert_eq!(memory_reading(stopped, Some(GARAGE_MEM_TOTAL)), None);
    }

    #[test]
    fn memory_subtracts_inactive_file_against_the_limit() {
        let t0 = garage_stats(STATS_T0);
        // hsm app: 267_550_720 usage − 57_462_784 inactive_file over a 256 MiB limit.
        let reading =
            memory_reading(by_prefix(&t0, "c5abbadb3674"), Some(GARAGE_MEM_TOTAL)).unwrap();
        assert_eq!(reading.used, 267_550_720 - 57_462_784);
        assert_eq!(reading.limit, 268_435_456);
        assert!(!reading.unlimited);
        let service = service_memory(&[reading], Some(GARAGE_MEM_TOTAL)).unwrap();
        assert!(
            (service.used_percent - 78.265).abs() < 0.01,
            "{}",
            service.used_percent
        );
        assert_eq!(limit_megabytes(service.limit_bytes), 256);
    }

    #[test]
    fn an_unlimited_container_is_measured_against_host_memory() {
        let stats: ContainerStats = serde_json::from_value(serde_json::json!({
            "memory_stats": {
                "usage": 2_147_483_648u64,
                "limit": 16_690_044_928u64,
                "stats": { "inactive_file": 1_073_741_824u64 }
            }
        }))
        .unwrap();
        let reading = memory_reading(&stats, Some(GARAGE_MEM_TOTAL)).unwrap();
        assert!(reading.unlimited);
        assert_eq!(reading.limit, GARAGE_MEM_TOTAL);
        let service = service_memory(&[reading], Some(GARAGE_MEM_TOTAL)).unwrap();
        assert!(service.unlimited);
        assert_eq!(limit_megabytes(service.limit_bytes), 15_916);
        assert!(
            (service.used_percent - 6.433).abs() < 0.01,
            "{}",
            service.used_percent
        );
    }

    #[test]
    fn cgroup_v1_inactive_file_is_honored_and_never_underflows() {
        let v1: ContainerStats = serde_json::from_value(serde_json::json!({
            "memory_stats": { "usage": 1000u64, "limit": 4000u64,
                              "stats": { "total_inactive_file": 400u64 } }
        }))
        .unwrap();
        assert_eq!(memory_reading(&v1, None).unwrap().used, 600);
        let odd: ContainerStats = serde_json::from_value(serde_json::json!({
            "memory_stats": { "usage": 100u64, "limit": 4000u64,
                              "stats": { "inactive_file": 400u64 } }
        }))
        .unwrap();
        assert_eq!(memory_reading(&odd, None).unwrap().used, 100);
        let zero_limit: ContainerStats = serde_json::from_value(serde_json::json!({
            "memory_stats": { "usage": 100u64, "limit": 0u64 }
        }))
        .unwrap();
        assert_eq!(memory_reading(&zero_limit, None), None);
    }

    #[test]
    fn replicas_are_summed_and_capped_at_the_host() {
        let a = MemoryReading {
            used: 100,
            limit: 1_000,
            unlimited: false,
        };
        let b = MemoryReading {
            used: 300,
            limit: 1_000,
            unlimited: false,
        };
        let service = service_memory(&[a, b], Some(10_000)).unwrap();
        assert_eq!(service.limit_bytes, 2_000);
        assert!((service.used_percent - 20.0).abs() < 1e-9);
        // Two unlimited replicas: each "limit" is the host; the pair cannot exceed it either.
        let host = MemoryReading {
            used: 1_000,
            limit: 10_000,
            unlimited: true,
        };
        let service = service_memory(&[host, host], Some(10_000)).unwrap();
        assert_eq!(service.limit_bytes, 10_000);
        assert!((service.used_percent - 20.0).abs() < 1e-9);
        assert!(service.unlimited);
        assert_eq!(service_memory(&[], Some(10_000)), None);
    }

    #[test]
    fn mem_total_parses_from_meminfo() {
        let text = "MemTotal:       16298872 kB\nMemFree:          812344 kB\n";
        assert_eq!(parse_mem_total(text), Some(GARAGE_MEM_TOTAL));
        assert_eq!(parse_mem_total("MemFree: 1 kB\n"), None);
        assert_eq!(parse_mem_total("MemTotal: lots kB\n"), None);
    }
}
