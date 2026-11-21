//! The container's own cgroup v2 memory files (#1818): `memory.events`, `memory.pressure` and
//! `memory.swap.current`, read straight from the cgroup filesystem.
//!
//! **Source.** The probe runs as a host service; `/sys/fs/cgroup` is readable to it (the unit's
//! `ProtectControlGroups=yes` makes it read-only, and these files are `0444`/`0644`), so no new
//! privilege and no new Engine API endpoint is needed. A container's directory is
//! `system.slice/docker-<id>.scope/` under the systemd cgroup driver and `docker/<id>/` under the
//! cgroupfs driver. Neither exists on cgroup v1 (Docker Desktop on WSL2 is v1), under rootless
//! Docker (its containers live in the user's slice) or for a container that is not running: such a
//! container has no cgroup memory files, and the sensors that need them are not registered for it.
//! Only a container id from the daemon's own list, checked to be a plain hex id, is spliced into a
//! path.
//!
//! **What is read.** `memory.events` `max` (how often the cgroup reached `memory.max`) feeds
//! `Memory limit hits per hour`, its `oom_kill` latches `OOM killed`; the `some … total=<µs>` line of
//! `memory.pressure` (PSI: time at least one task stalled on memory) feeds `Memory pressure %`;
//! `memory.swap.current` (bytes) feeds `Swap used`. A file that is missing or does not parse is
//! "unknown", never 0.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::Duration;

use super::contract;
use super::stats::CpuSkip;

/// Where the container cgroup directories are looked up: the cgroup v2 mount (`/sys/fs/cgroup`) in
/// production, a fake tree in tests. `None` reads nothing.
#[derive(Clone, Debug, Default)]
pub struct CgroupFs {
    root: Option<PathBuf>,
}

impl CgroupFs {
    pub fn new(root: Option<PathBuf>) -> Self {
        Self { root }
    }

    /// The container's cgroup directory: the systemd driver's layout first, then cgroupfs'.
    /// `None` when neither exists, or when `id` is not a plain hex container id.
    pub fn dir(&self, id: &str) -> Option<PathBuf> {
        let root = self.root.as_ref()?;
        if id.is_empty() || id.len() > 64 || !id.bytes().all(|b| b.is_ascii_hexdigit()) {
            return None;
        }
        [
            root.join("system.slice").join(format!("docker-{id}.scope")),
            root.join("docker").join(id),
        ]
        .into_iter()
        .find(|dir| dir.is_dir())
    }

    /// The memory files of the container's cgroup; `None` when it has no cgroup v2 directory.
    pub fn read(&self, id: &str) -> Option<CgroupMemory> {
        let dir = self.dir(id)?;
        Some(CgroupMemory {
            events: read(&dir, "memory.events").map(|text| parse_events(&text)),
            pressure_some_us: read(&dir, "memory.pressure")
                .as_deref()
                .and_then(parse_pressure_some_total),
            swap_bytes: read(&dir, "memory.swap.current")
                .as_deref()
                .and_then(parse_swap_current),
        })
    }

    #[cfg(test)]
    pub fn root(&self) -> Option<&Path> {
        self.root.as_deref()
    }
}

fn read(dir: &Path, file: &str) -> Option<String> {
    std::fs::read_to_string(dir.join(file)).ok()
}

/// One read of a container's cgroup memory files. `None` fields: the file is absent (no swap
/// accounting, no PSI) or did not parse.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct CgroupMemory {
    pub events: Option<MemoryEvents>,
    /// `memory.pressure` `some total=`, microseconds.
    pub pressure_some_us: Option<u64>,
    /// `memory.swap.current`, bytes.
    pub swap_bytes: Option<u64>,
}

/// The two `memory.events` counters the probe reads, cumulative since the cgroup was created.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct MemoryEvents {
    /// Times the cgroup's usage reached `memory.max` (and reclaim was forced).
    pub max: Option<u64>,
    /// Processes of the cgroup killed by the OOM killer.
    pub oom_kill: Option<u64>,
}

/// `memory.events`: `<key> <count>` lines. A line that does not parse is ignored.
pub fn parse_events(text: &str) -> MemoryEvents {
    let mut events = MemoryEvents::default();
    for line in text.lines() {
        let mut parts = line.split_whitespace();
        let (Some(key), Some(value), None) = (parts.next(), parts.next(), parts.next()) else {
            continue;
        };
        let Ok(value) = value.parse::<u64>() else {
            continue;
        };
        match key {
            "max" => events.max = Some(value),
            "oom_kill" => events.oom_kill = Some(value),
            _ => {}
        }
    }
    events
}

/// The `total=` of the `some` line of `memory.pressure`, microseconds.
pub fn parse_pressure_some_total(text: &str) -> Option<u64> {
    text.lines().find_map(|line| {
        let mut parts = line.split_whitespace();
        if parts.next() != Some("some") {
            return None;
        }
        parts.find_map(|field| field.strip_prefix("total=")?.parse().ok())
    })
}

/// `memory.swap.current`: one number, bytes.
pub fn parse_swap_current(text: &str) -> Option<u64> {
    text.trim().parse().ok()
}

/// One `memory.pressure` observation of a container.
#[derive(Clone, Debug, PartialEq)]
pub struct PressureCounter {
    /// `some total`, microseconds.
    pub total_us: u64,
    /// When (monotonic) it was read.
    pub at: Duration,
    /// The container's `State.StartedAt` then: a different one is a new cgroup.
    pub started_at: Option<String>,
}

/// Percent of the wall time between two samples in which some task of the cgroup stalled on
/// memory: Δtotal(µs) / Δt × 100, clamped to 0…100. The skip rules are CPU's
/// ([`super::stats::cpu_percent`]): a counter that went backwards and an interval outside
/// [`contract::MIN_INTERVAL_FACTOR`]…[`contract::MAX_INTERVAL_FACTOR`] sample periods give no
/// value; a restart (another start time) is a new baseline.
pub fn pressure_percent(
    previous: &PressureCounter,
    current: &PressureCounter,
    sample_period: Duration,
) -> Result<f64, CpuSkip> {
    if let (Some(before), Some(now)) = (&previous.started_at, &current.started_at) {
        if before != now {
            return Err(CpuSkip::FirstSample);
        }
    }
    if current.total_us < previous.total_us {
        return Err(CpuSkip::CounterReset);
    }
    let interval = current.at.saturating_sub(previous.at).as_secs_f64();
    let period = sample_period.as_secs_f64();
    if interval <= 0.0
        || interval < period * contract::MIN_INTERVAL_FACTOR
        || interval > period * contract::MAX_INTERVAL_FACTOR
    {
        return Err(CpuSkip::AbnormalInterval);
    }
    let stalled = (current.total_us - previous.total_us) as f64 / 1_000_000.0;
    Ok((stalled / interval * 100.0).clamp(0.0, 100.0))
}

/// The last `memory.pressure` counter of every container, keyed by container id (a new id has no
/// entry, so its first sample is skipped) — [`super::stats::CpuTracker`]'s shape.
#[derive(Debug, Default)]
pub struct PressureTracker {
    last: HashMap<String, PressureCounter>,
}

impl PressureTracker {
    /// Record `current` and return the percent since the previous sample; the new counter always
    /// becomes the baseline.
    pub fn sample(
        &mut self,
        container_id: &str,
        current: PressureCounter,
        sample_period: Duration,
    ) -> Result<f64, CpuSkip> {
        let outcome = match self.last.get(container_id) {
            None => Err(CpuSkip::FirstSample),
            Some(previous) => pressure_percent(previous, &current, sample_period),
        };
        self.last.insert(container_id.to_string(), current);
        outcome
    }

    pub fn forget(&mut self, container_id: &str) {
        self.last.remove(container_id);
    }

    pub fn retain(&mut self, mut keep: impl FnMut(&str) -> bool) {
        self.last.retain(|id, _| keep(id));
    }
}

/// The last `oom_kill` count of every container, in memory only: a kill while the probe was not
/// running is seen only through `State.OOMKilled`, i.e. only when it ended the container.
#[derive(Debug, Default)]
pub struct OomKillTracker {
    last: HashMap<String, (u64, Option<String>)>,
}

impl OomKillTracker {
    /// Record the container's `oom_kill` count; true when it grew since the previous sample of
    /// the same container start. The first sample, a restart (another start time) and a counter
    /// that went backwards only set the baseline.
    pub fn grew(&mut self, container_id: &str, count: u64, started_at: Option<&str>) -> bool {
        let previous = self.last.insert(
            container_id.to_string(),
            (count, started_at.map(str::to_string)),
        );
        let Some((before, then)) = previous else {
            return false;
        };
        let same_start = match (then.as_deref(), started_at) {
            (Some(then), Some(now)) => then == now,
            _ => true,
        };
        same_start && count > before
    }

    pub fn forget(&mut self, container_id: &str) {
        self.last.remove(container_id);
    }

    pub fn retain(&mut self, mut keep: impl FnMut(&str) -> bool) {
        self.last.retain(|id, _| keep(id));
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::probe_only::host::tests::FakeTree;

    const PERIOD: Duration = Duration::from_secs(60);

    /// The cgroup v2 captures of the demo containers at the two stats rounds, as
    /// `fixtures/docker/demo/cgroup-t<round>/<container id>/<file>` (the real files' format, values
    /// chosen per container): hsm/app swaps and hits its limit, lumina-ci/janitor hits its limit
    /// and has a process OOM-killed while it keeps running, lumina/mdb has no `memory.pressure`
    /// (no PSI), panelhost has no cgroup directory at all (the cgroup v1 / rootless shape).
    fn fixture_round(round: usize) -> PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("fixtures/docker/demo")
            .join(format!("cgroup-t{round}"))
    }

    /// The demo container whose directory uses the cgroupfs driver's layout (`docker/<id>`); the
    /// others use the systemd driver's (`system.slice/docker-<id>.scope`).
    pub const CGROUPFS_LAYOUT: &str = "8a83c2a538bf";

    /// Lay the demo captures of `round` out under `relative` in `tree` as a cgroup v2 mount would
    /// hold them, replacing an earlier round's files.
    pub fn demo_cgroups(tree: &FakeTree, relative: &str, round: usize) {
        for entry in std::fs::read_dir(fixture_round(round)).expect("cgroup fixtures") {
            let entry = entry.unwrap();
            let id = entry.file_name().into_string().unwrap();
            let dir = if id.starts_with(CGROUPFS_LAYOUT) {
                format!("{relative}/docker/{id}")
            } else {
                format!("{relative}/system.slice/docker-{id}.scope")
            };
            for file in std::fs::read_dir(entry.path()).unwrap() {
                let file = file.unwrap();
                let text = std::fs::read_to_string(file.path()).unwrap();
                tree.file(
                    &format!("{dir}/{}", file.file_name().into_string().unwrap()),
                    &text,
                );
            }
        }
    }

    const APP: &str = "c5abbadb3674293572f199c8082b06f6e55c5dbaa2606139fb280ec9fc2f056b";
    const CPROXY: &str = "8a83c2a538bfd074fe7d57f8cfda1b4552f930c8702473511d3ce7a952aed0ae";
    const MDB: &str = "82274bb7d70e0e462117d8d1ec10b4b5d3a34ff442f557c1015d21743f3ddd7e";
    const PANELHOST: &str = "7717692f78a805a72da0f7611b049f4a857c8e71e193619f5c2f5fc24a8a4498";

    #[test]
    fn the_real_file_formats_parse() {
        let events = "low 0\nhigh 0\nmax 957\noom 0\noom_kill 0\noom_group_kill 0\n";
        assert_eq!(
            parse_events(events),
            MemoryEvents {
                max: Some(957),
                oom_kill: Some(0)
            }
        );
        let pressure = "some avg10=0.00 avg60=0.00 avg300=0.00 total=1240\n\
                        full avg10=0.00 avg60=0.00 avg300=0.00 total=1240\n";
        assert_eq!(parse_pressure_some_total(pressure), Some(1240));
        assert_eq!(parse_swap_current("1671168\n"), Some(1_671_168));
    }

    #[test]
    fn malformed_lines_are_unknown_not_zero() {
        // Garbage, a missing value, a non-number, an extra field: each line is skipped alone.
        let events = "max\noom_kill x\nmax 3 4\nlow 1\noom_kill 2\r\n";
        assert_eq!(
            parse_events(events),
            MemoryEvents {
                max: None,
                oom_kill: Some(2)
            }
        );
        assert_eq!(parse_events(""), MemoryEvents::default());
        // No `some` line (only `full`), or a `some` line without a parsable total.
        assert_eq!(
            parse_pressure_some_total("full avg10=0.00 avg60=0.00 avg300=0.00 total=99\n"),
            None
        );
        assert_eq!(
            parse_pressure_some_total("some avg10=0.00 total=lots\n"),
            None
        );
        assert_eq!(parse_pressure_some_total(""), None);
        // `full` before `some` is still the `some` line's total.
        assert_eq!(
            parse_pressure_some_total("full total=1\nsome avg10=1.00 total=7\n"),
            Some(7)
        );
        assert_eq!(parse_swap_current("max\n"), None);
        assert_eq!(parse_swap_current(""), None);
        assert_eq!(parse_swap_current("-1\n"), None);
    }

    #[test]
    fn both_driver_layouts_are_found_and_a_missing_directory_is_none() {
        let tree = FakeTree::new("cgroup-layouts");
        demo_cgroups(&tree, "cgroup", 0);
        let cgroups = CgroupFs::new(Some(tree.0.join("cgroup")));
        // systemd driver
        assert!(cgroups
            .dir(APP)
            .unwrap()
            .ends_with(format!("docker-{APP}.scope")));
        let app = cgroups.read(APP).unwrap();
        assert_eq!(app.events.unwrap().max, Some(957));
        assert_eq!(app.pressure_some_us, Some(1240));
        assert_eq!(app.swap_bytes, Some(1_671_168));
        // cgroupfs driver
        assert!(cgroups
            .dir(CPROXY)
            .unwrap()
            .ends_with(format!("docker/{CPROXY}")));
        assert_eq!(cgroups.read(CPROXY).unwrap().swap_bytes, Some(0));
        // No PSI: the directory is there, the pressure file is not.
        let mdb = cgroups.read(MDB).unwrap();
        assert_eq!(mdb.pressure_some_us, None);
        assert!(mdb.events.is_some());
        // cgroup v1 / rootless: no directory at all.
        assert_eq!(cgroups.read(PANELHOST), None);
        // Nothing that is not a hex id is ever spliced into a path.
        let long = "a".repeat(65);
        for id in ["", "../../etc", "abc/def", long.as_str(), "docker-x.scope"] {
            assert_eq!(cgroups.dir(id), None, "{id}");
        }
        // No root: nothing is read.
        assert_eq!(CgroupFs::default().read(APP), None);
        assert_eq!(cgroups.root(), Some(tree.0.join("cgroup").as_path()));
    }

    fn counter(total_us: u64, at_secs: u64, started: Option<&str>) -> PressureCounter {
        PressureCounter {
            total_us,
            at: Duration::from_secs(at_secs),
            started_at: started.map(str::to_string),
        }
    }

    #[test]
    fn pressure_is_the_stalled_share_of_the_interval() {
        // 1.8 s of stall in 60 s: 3 %.
        let percent = pressure_percent(
            &counter(1_240, 0, None),
            &counter(1_801_240, 60, None),
            PERIOD,
        );
        assert!((percent.unwrap() - 3.0).abs() < 1e-9);
        // No stall is a real 0.
        assert_eq!(
            pressure_percent(&counter(5, 0, None), &counter(5, 60, None), PERIOD),
            Ok(0.0)
        );
        // Never above 100.
        assert_eq!(
            pressure_percent(&counter(0, 0, None), &counter(90_000_000, 60, None), PERIOD),
            Ok(100.0)
        );
    }

    #[test]
    fn pressure_skips_like_cpu() {
        let mut tracker = PressureTracker::default();
        let started = Some("2026-09-28T08:00:00Z");
        assert_eq!(
            tracker.sample("a", counter(100, 0, started), PERIOD),
            Err(CpuSkip::FirstSample)
        );
        assert!(tracker
            .sample("a", counter(600_100, 60, started), PERIOD)
            .is_ok());
        // Counter backwards: skipped, and the new value is the baseline.
        assert_eq!(
            tracker.sample("a", counter(10, 120, started), PERIOD),
            Err(CpuSkip::CounterReset)
        );
        assert!(tracker
            .sample("a", counter(10, 180, started), PERIOD)
            .is_ok());
        // A restart whose new counter already passed the old one is still a baseline.
        let again = Some("2026-09-28T09:00:00Z");
        assert_eq!(
            tracker.sample("a", counter(900, 240, again), PERIOD),
            Err(CpuSkip::FirstSample)
        );
        // Too soon or too late (after an outage).
        for at in [269, 600] {
            assert_eq!(
                tracker.sample("a", counter(1_000, at, again), PERIOD),
                Err(CpuSkip::AbnormalInterval),
                "{at}"
            );
        }
        tracker.retain(|id| id != "a");
        assert_eq!(
            tracker.sample("a", counter(1_000, 700, again), PERIOD),
            Err(CpuSkip::FirstSample)
        );
        tracker.forget("a");
        assert_eq!(
            tracker.sample("a", counter(1_000, 760, again), PERIOD),
            Err(CpuSkip::FirstSample)
        );
    }

    #[test]
    fn an_oom_kill_counts_only_between_samples_of_the_same_start() {
        let mut kills = OomKillTracker::default();
        let started = Some("2026-09-28T08:00:00Z");
        // First sight: a baseline, even with kills from before the probe was watching.
        assert!(!kills.grew("a", 3, started));
        assert!(!kills.grew("a", 3, started));
        assert!(kills.grew("a", 4, started));
        assert!(!kills.grew("a", 4, started));
        // Restarted: a new cgroup, counted again from 0 — a baseline, not a kill.
        let again = Some("2026-09-28T09:00:00Z");
        assert!(!kills.grew("a", 5, again));
        assert!(kills.grew("a", 6, again));
        // A counter that went backwards is a baseline too.
        assert!(!kills.grew("a", 0, again));
        assert!(kills.grew("a", 1, again));
        // Without a known start time the counter alone decides.
        assert!(kills.grew("a", 2, None));
        kills.retain(|id| id != "a");
        assert!(!kills.grew("a", 9, again));
        kills.forget("a");
        assert!(!kills.grew("a", 10, again));
    }
}
