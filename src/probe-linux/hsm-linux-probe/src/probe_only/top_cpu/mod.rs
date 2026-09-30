//! Top CPU processes (#1479): `.computer/Top CPU processes/<name>`, one Double sensor per busy
//! process name — the Linux side of the Windows agents' sensor family.
//!
//! # Placement
//!
//! Probe-only, by the owner's standing rule for the Linux epic (#1413): new Linux sources live in
//! the probe; moving one into the shared collector catalog and the managed collector is a later,
//! separate step. Such a move must mirror a managed Unix implementation over the same `/proc`
//! source (root CLAUDE.md rule #10) and lock the two with a conformance scenario (rule #9).
//!
//! # Wire contract — the Windows one, so alert templates carry over unchanged
//!
//! Every value below is copied from the two Windows implementations, which are wire-identical:
//! `src/native/collector/src/cpu_top.cpp` + `RunTopCpuLoop` in `hsm_collector.cpp` (what HsmAgent
//! runs) and `src/collector/HSMDataCollector/DefaultSensors/Windows/Process/WindowsTopCpuMonitor.cs`.
//! The scenario `tests/conformance/collector/top_cpu_contract.hsmtest` pins the path shape
//! (`<ComputerName>/.computer/Top CPU processes/<exe-name>`, Double) and the `min_percent` meaning
//! ("0 = any positive delta").
//!
//! | | Windows | here |
//! |---|---|---|
//! | path | `RevealDefaultPath("Top CPU processes", name, is_computer_sensor = true)` | [`sensor_path`] with `is_computer_sensor` |
//! | type | Double (instant) | [`DoubleSensor`] |
//! | unit | `Unit.Percents` (100) | [`UNIT_PERCENTS`] |
//! | TTL | 5 min | [`TTL`] |
//! | EnableGrafana | true | true |
//! | statistics, KeepHistory, alerts | none / server default / none | the same |
//! | description | `Top **<count>** CPU consumers by % of machine CPU` + a path line | [`description`] |
//! | value | % of the whole machine, summed over a name's processes | [`procfs::Sampler`] |
//! | rule | ≥ `minPercent`, busiest `count`, ties by name, one post per `periodMs` | [`procfs::select_top`] |
//! | name cap | `max(count × 8, 64)` distinct names, then new names are skipped | [`max_tracked_names`] |
//!
//! One field cannot be mirrored: Windows registers `DisplayUnit: null`, while an instant sensor
//! created through the C ABI (`hsm_collector_create_sensor_with_options`) always sends `0`. The
//! server reads `DisplayUnit` only for Rate sensors, so a Double sensor behaves the same.
//!
//! # Cost
//!
//! One record per minute per posted name: ≈ 1 440 records/day for each process that stays at or
//! above 1 % of the host, nothing for the others. `count` bounds it at ≈ 14 400/day.
//!
//! # Names
//!
//! See [`procfs::sensor_name`]: `comm` (≤ 15 bytes, the kernel's truncation), normalized to the
//! characters the server's alert-template wildcard matches; a kernel thread is named by the part
//! before its first `/` (`kworker/3:1-events` → `kworker`), so per-CPU threads are one sensor.

pub mod procfs;

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::Duration;

use hsm_collector::{Collector, DoubleSensor, SensorOptions};

use self::procfs::{Sampler, Usage};
use super::{FailureLog, Source};
use crate::config::TopCpuConfig;
use crate::logging::Logger;

/// Where the processes are read from in production.
pub const PROC_ROOT: &str = "/proc";

/// `.computer/Top CPU processes` — the Windows `RevealDefaultPath(…, is_computer_sensor = true)`.
pub const CATEGORY: &str = ".computer/Top CPU processes";

/// `WindowsTopCpuMonitor.TemplateOptions.TTL` / `cpu_top` `opts.ttl_ms = 300000`: a name that
/// stops being posted (the process went idle or exited) turns to Timeout after 5 minutes.
pub const TTL: Duration = Duration::from_secs(5 * 60);

/// `Unit.Percents` (Windows: `SensorUnit = Unit.Percents` / `opts.unit = 100`).
pub const UNIT_PERCENTS: i32 = 100;

/// The Windows path line for a process whose executable cannot be named. Also true here for a
/// kernel thread, which has no executable. ASCII `-`, as on Windows.
const SYSTEM_PROCESS_PATH: &str = "\n\n**Path:** _(system process - path unavailable)_";
/// Linux only: `/proc/<pid>/exe` of another user's process is not readable without
/// `CAP_SYS_PTRACE`, which the unprivileged probe does not have. Calling that a "system process"
/// would be wrong for most of them.
const OTHER_USER_PATH: &str = "\n\n**Path:** _(another user's process - path unavailable)_";

const WHAT: &str = "top CPU processes";

pub fn sensor_path(name: &str) -> String {
    format!("{CATEGORY}/{name}")
}

/// `max(count × 8, 64)`: the Windows bound on distinct names (the server has no sensor delete, so
/// a host churning through distinctly named processes must not grow the tree without limit).
pub fn max_tracked_names(count: usize) -> usize {
    count.saturating_mul(8).max(64)
}

/// The Windows description: `Top **<count>** CPU consumers by % of machine CPU` and the path line.
pub fn description(count: usize, path_line: &str) -> String {
    format!("Top **{count}** CPU consumers by % of machine CPU{path_line}")
}

/// The path line for the process a sensor is created for: its executable, the Windows "system
/// process" note for a kernel thread, a note for another user's process, and nothing when the
/// process exited before it could be looked at (the managed "never looked up" case).
fn path_line(proc_root: &Path, usage: &Usage) -> String {
    if usage.kernel_thread {
        return SYSTEM_PROCESS_PATH.to_string();
    }
    match std::fs::read_link(proc_root.join(usage.pid.to_string()).join("exe")) {
        Ok(path) => format!("\n\n**Path:** `{}`", path.display()),
        Err(error) if error.kind() == std::io::ErrorKind::PermissionDenied => {
            OTHER_USER_PATH.to_string()
        }
        Err(_) => String::new(),
    }
}

/// Register the source when `topCpu.enabled`. No sensor exists before the first measured
/// interval: each busy name registers at runtime (collector ≥ 0.9.1 posts the registration).
pub fn register<'c>(
    collector: &'c Collector,
    config: &TopCpuConfig,
    proc_root: &Path,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    if !config.enabled {
        logger.info("top CPU processes: off (topCpu.enabled = false)");
        return None;
    }
    let (count, min_percent, period) = (config.count(), config.min_percent, config.period());
    logger.info(format!(
        "top CPU processes: every {} s, the {count} busiest process names at or above \
         {min_percent} % of the host, under {CATEGORY}/<name>",
        period.as_secs_f64()
    ));
    // Degrades, never fails: with hidepid only the probe's own processes are visible.
    let mountinfo = std::fs::read_to_string(proc_root.join("self/mountinfo")).unwrap_or_default();
    if let Some(option) = procfs::hidepid_option(&mountinfo, &proc_root.to_string_lossy()) {
        logger.info(format!(
            "top CPU processes: {} is mounted with {option} (systemd ProtectProc=), so only this \
             service's own processes are visible and the sensors cannot show the host's; the \
             drop-in `[Service] ProtectProc=default` lifts it (README, Top CPU processes)",
            proc_root.display()
        ));
    }
    Some(Box::new(TopCpu {
        collector,
        sampler: Sampler::new(proc_root),
        count,
        min_percent,
        period,
        max_tracked: max_tracked_names(count),
        sensors: HashMap::new(),
        cap_logged: false,
        baseline_logged: false,
        failures: FailureLog::default(),
        registration_failures: FailureLog::default(),
        post_failures: FailureLog::default(),
    }))
}

struct TopCpu<'c> {
    collector: &'c Collector,
    sampler: Sampler,
    count: usize,
    min_percent: f64,
    period: Duration,
    max_tracked: usize,
    /// name → sensor, created on the name's first post and kept for the probe's lifetime.
    sensors: HashMap<String, DoubleSensor<'c>>,
    cap_logged: bool,
    baseline_logged: bool,
    failures: FailureLog,
    registration_failures: FailureLog,
    post_failures: FailureLog,
}

impl TopCpu<'_> {
    fn proc_root(&self) -> PathBuf {
        self.sampler.proc_root().to_path_buf()
    }

    fn post(&mut self, usage: &Usage, logger: &Logger) {
        if !self.sensors.contains_key(&usage.name) {
            if self.sensors.len() >= self.max_tracked {
                if !self.cap_logged {
                    logger.warn(format!(
                        "top CPU processes: {} distinct names have a sensor, the cap; newly busy \
                         names get none (the tracked ones keep reporting)",
                        self.max_tracked
                    ));
                    self.cap_logged = true;
                }
                return;
            }
            let path = sensor_path(&usage.name);
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_ttl(TTL)
                .with_unit(UNIT_PERCENTS)
                .with_enable_grafana(true)
                .with_description(description(
                    self.count,
                    &path_line(&self.proc_root(), usage),
                ));
            match self.collector.double_sensor(&path, &options) {
                Ok(sensor) => {
                    self.registration_failures
                        .succeeded(logger, "top CPU processes: registration");
                    self.sensors.insert(usage.name.clone(), sensor);
                }
                Err(error) => {
                    // Not cached: the next interval tries again.
                    self.registration_failures.failed(
                        logger,
                        "top CPU processes: registration",
                        &format!("cannot register {path}: {error}"),
                    );
                    return;
                }
            }
        }
        let Some(sensor) = self.sensors.get(&usage.name) else {
            return;
        };
        match sensor.add(usage.percent) {
            Ok(()) => self
                .post_failures
                .succeeded(logger, "top CPU processes: post"),
            Err(error) => self.post_failures.failed(
                logger,
                "top CPU processes: post",
                &format!("cannot post {}: {error}", sensor_path(&usage.name)),
            ),
        }
    }
}

impl Source for TopCpu<'_> {
    fn name(&self) -> &'static str {
        "top cpu"
    }

    fn period(&self) -> Duration {
        self.period
    }

    /// The first call (right after Start) only takes the baseline, as on Windows; every later
    /// one posts the interval since the previous call.
    fn sample(&mut self, logger: &Logger) {
        match self.sampler.sample() {
            Ok((by_name, visibility)) => {
                self.failures.succeeded(logger, WHAT);
                if !self.baseline_logged {
                    logger.info(format!(
                        "top CPU processes: baseline over {} visible processes",
                        visibility.processes
                    ));
                    self.baseline_logged = true;
                }
                for usage in procfs::select_top(&by_name, self.count, self.min_percent) {
                    self.post(&usage, logger);
                }
            }
            Err(reason) => self.failures.failed(logger, WHAT, &reason),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::procfs::tests::{fake_proc, process};
    use super::*;
    use crate::logging::Level;
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;
    use std::sync::{Arc, Mutex};

    fn enabled() -> TopCpuConfig {
        TopCpuConfig {
            enabled: true,
            ..TopCpuConfig::default()
        }
    }

    fn capture() -> (Logger, Arc<Mutex<Vec<String>>>) {
        let lines = Arc::new(Mutex::new(Vec::new()));
        let logger = Logger::with_sink(Level::Debug, {
            let lines = Arc::clone(&lines);
            move |line: &str| lines.lock().unwrap().push(line.to_string())
        });
        (logger, lines)
    }

    fn top_cpu_registrations(collector: &Collector) -> Vec<String> {
        collector
            .registrations()
            .into_iter()
            .filter(|json| json.contains("\"Path\":\".computer/Top CPU processes/"))
            .collect()
    }

    #[test]
    fn off_unless_enabled_like_the_agent() {
        let collector = test_collector();
        let (logger, lines) = capture();
        let tree = FakeTree::new("topcpu-off");
        assert!(register(&collector, &TopCpuConfig::default(), &tree.0, &logger).is_none());
        assert!(lines.lock().unwrap().iter().any(|l| l.contains("off")));
    }

    /// The Windows wire shape, registered at runtime for the busy names only.
    #[test]
    fn busy_names_register_at_runtime_with_the_windows_shape() {
        let tree = FakeTree::new("topcpu-wire");
        fake_proc(&tree, 0);
        process(&tree, 100, "postgres", 0, 0, 50);
        process(&tree, 101, "postgres", 0, 0, 51);
        process(&tree, 200, "idle-daemon", 0, 0, 60);
        process(&tree, 300, "kworker/0:1-events", 0x0020_0000, 0, 1);
        let collector = test_collector();
        let (logger, _) = capture();
        let mut source = register(&collector, &enabled(), &tree.0, &logger).expect("source");
        assert_eq!(source.period(), Duration::from_secs(60));
        collector.start().expect("start");

        source.sample(&logger);
        assert!(
            top_cpu_registrations(&collector).is_empty(),
            "the baseline registers nothing"
        );

        fake_proc(&tree, 10_000);
        process(&tree, 100, "postgres", 0, 1_500, 50);
        process(&tree, 101, "postgres", 0, 500, 51);
        process(&tree, 200, "idle-daemon", 0, 99, 60); // 0.99 %: below the 1 % floor
        process(&tree, 300, "kworker/0:1-events", 0x0020_0000, 300, 1);
        source.sample(&logger);

        let registrations = top_cpu_registrations(&collector);
        collector.stop().expect("stop");
        let find = |name: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("\"Path\":\"{}\"", sensor_path(name))))
                .unwrap_or_else(|| panic!("{name} not registered: {registrations:#?}"))
                .clone()
        };
        assert_eq!(registrations.len(), 2, "{registrations:#?}");
        for json in [find("postgres"), find("kworker")] {
            for part in [
                "\"SensorType\":2,",
                "\"OriginalUnit\":100,",
                "\"TTLTicks\":[3000000000]",
                "\"EnableGrafana\":true",
                "\"IsSingletonSensor\":true",
                "\"Statistics\":0",
                "\"KeepHistory\":null",
                "\"Alerts\":null",
                "Top **10** CPU consumers by % of machine CPU",
            ] {
                assert!(json.contains(part), "missing {part} in {json}");
            }
        }
        // The fake tree has no exe links: the process "exited" before it was looked at, so no
        // path line; a kernel thread gets the Windows "system process" line.
        assert!(!find("postgres").contains("**Path:**"));
        assert!(find("kworker").contains("_(system process - path unavailable)_"));
    }

    #[test]
    #[cfg(unix)]
    fn the_description_names_the_executable() {
        let tree = FakeTree::new("topcpu-exe");
        tree.link("100/exe", "/usr/lib/postgresql/17/bin/postgres");
        let usage = Usage {
            name: "postgres".into(),
            percent: 25.0,
            pid: 100,
            kernel_thread: false,
        };
        assert_eq!(
            description(10, &path_line(&tree.0, &usage)),
            "Top **10** CPU consumers by % of machine CPU\n\n**Path:** \
             `/usr/lib/postgresql/17/bin/postgres`"
        );
    }

    #[test]
    fn new_names_stop_at_the_cap_and_the_tracked_ones_keep_reporting() {
        assert_eq!(max_tracked_names(10), 80);
        assert_eq!(max_tracked_names(1), 64);
        assert_eq!(max_tracked_names(usize::MAX), usize::MAX);

        let tree = FakeTree::new("topcpu-cap");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = TopCpu {
            collector: &collector,
            sampler: Sampler::new(&tree.0),
            count: 70,
            min_percent: 1.0,
            period: Duration::from_secs(60),
            max_tracked: 2,
            sensors: HashMap::new(),
            cap_logged: false,
            baseline_logged: false,
            failures: FailureLog::default(),
            registration_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        };
        collector.start().expect("start");
        for name in ["a", "b", "c", "d", "a"] {
            let usage = Usage {
                name: name.into(),
                percent: 5.0,
                pid: 1,
                kernel_thread: false,
            };
            source.post(&usage, &logger);
        }
        collector.stop().expect("stop");
        assert_eq!(source.sensors.len(), 2);
        assert!(source.sensors.contains_key("a") && source.sensors.contains_key("b"));
        let warned = lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains("the cap"))
            .count();
        assert_eq!(warned, 1, "logged once");
    }

    #[test]
    fn hidepid_is_one_info_line_not_a_failure() {
        let tree = FakeTree::new("topcpu-hidepid");
        let proc_root = tree.0.to_string_lossy().to_string();
        tree.file(
            "self/mountinfo",
            &format!("812 22 0:77 / {proc_root} rw,nosuid - proc proc rw,hidepid=invisible\n"),
        );
        fake_proc(&tree, 0);
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = register(&collector, &enabled(), &tree.0, &logger).expect("source");
        source.sample(&logger);
        let lines = lines.lock().unwrap();
        let hidepid: Vec<&String> = lines.iter().filter(|l| l.contains("hidepid")).collect();
        assert_eq!(hidepid.len(), 1, "{lines:#?}");
        assert!(hidepid[0].contains("|INFO|"), "{}", hidepid[0]);
        assert!(!lines.iter().any(|l| l.contains("|ERROR|")), "{lines:#?}");
    }

    #[test]
    fn an_unreadable_proc_is_logged_once_and_posts_nothing() {
        let tree = FakeTree::new("topcpu-broken");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source =
            register(&collector, &enabled(), &tree.0.join("absent"), &logger).expect("source");
        source.sample(&logger);
        source.sample(&logger);
        let errors = lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains("|ERROR|"))
            .count();
        assert_eq!(errors, 1);
        assert!(top_cpu_registrations(&collector).is_empty());
    }
}
