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
/// `CAP_SYS_PTRACE`, which the unprivileged probe does not have, and its argv[0] was no absolute
/// path either. Calling that a "system process" would be wrong for most of them.
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

/// The longest path a description shows, in characters; longer ones are cut and end in `...`.
const MAX_PATH_CHARS: usize = 256;
/// How much of `/proc/<pid>/cmdline` is ever read: enough for argv[0], never the whole command.
const CMDLINE_READ_LIMIT: u64 = 4096;

/// The path line for the process a sensor is created for: its executable, the Windows "system
/// process" note for a kernel thread, and nothing when the process exited before it could be
/// looked at (the managed "never looked up" case). Another user's process — most of them, the
/// probe being unprivileged — has an unreadable `exe` link (it needs `CAP_SYS_PTRACE`); it is
/// named by its absolute argv[0] ([`argv0_path`]) and otherwise by a note saying so.
fn path_line(proc_root: &Path, usage: &Usage) -> String {
    if usage.kernel_thread {
        return SYSTEM_PROCESS_PATH.to_string();
    }
    let process_dir = proc_root.join(usage.pid.to_string());
    path_line_from(std::fs::read_link(process_dir.join("exe")), &process_dir)
}

fn path_line_from(exe: std::io::Result<PathBuf>, process_dir: &Path) -> String {
    let shown = |path: &str| format!("\n\n**Path:** `{}`", sanitize_path(path));
    match exe {
        Ok(path) => shown(&path.to_string_lossy()),
        Err(error) if error.kind() == std::io::ErrorKind::PermissionDenied => {
            match argv0_path(process_dir) {
                Some(argv0) => shown(&argv0),
                None => OTHER_USER_PATH.to_string(),
            }
        }
        Err(_) => String::new(),
    }
}

/// argv[0] from `<pid>/cmdline` — world-readable, unlike the `exe` link — when it is an absolute
/// path; `None` when it is relative, empty (a zombie or a kernel thread has an empty cmdline) or
/// unreadable. **Only argv[0] ever leaves this function**: the arguments can carry secrets. It is
/// cut at the first NUL *and* at the first whitespace, because a process that rewrites its title
/// (`setproctitle`, Chrome) may join its arguments with spaces into one NUL-free string; the cost
/// is that an executable path containing a space is shown only up to it. At most
/// [`CMDLINE_READ_LIMIT`] bytes are read.
fn argv0_path(process_dir: &Path) -> Option<String> {
    use std::io::Read;
    let mut bytes = Vec::new();
    std::fs::File::open(process_dir.join("cmdline"))
        .ok()?
        .take(CMDLINE_READ_LIMIT)
        .read_to_end(&mut bytes)
        .ok()?;
    let first = bytes.split(|byte| *byte == 0).next().unwrap_or_default();
    let text = String::from_utf8_lossy(first);
    let argv0 = text.split(char::is_whitespace).next().unwrap_or_default();
    if argv0.starts_with('/') {
        Some(argv0.to_string())
    } else {
        None
    }
}

/// A path as the description's code span shows it: control characters and backticks (which would
/// end the span) removed, at most [`MAX_PATH_CHARS`] characters.
fn sanitize_path(path: &str) -> String {
    let clean: String = path
        .chars()
        .filter(|c| !c.is_control() && *c != '`')
        .collect();
    if clean.chars().count() <= MAX_PATH_CHARS {
        clean
    } else {
        let cut: String = clean.chars().take(MAX_PATH_CHARS - 3).collect();
        format!("{cut}...")
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
        registration_failures: Episode::default(),
        post_failures: Episode::default(),
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
    registration_failures: Episode,
    post_failures: Episode,
}

impl TopCpu<'_> {
    fn proc_root(&self) -> PathBuf {
        self.sampler.proc_root().to_path_buf()
    }

    /// Post one sample's top names: register the new ones, add a value to each. Failures are
    /// counted per kind and logged once per episode ([`Tally::report`]), not once per name.
    fn post_all(&mut self, top: &[Usage], logger: &Logger) {
        let (mut registrations, mut posts) = (Tally::default(), Tally::default());
        for usage in top {
            self.post(usage, logger, &mut registrations, &mut posts);
        }
        registrations.report(
            &mut self.registration_failures,
            logger,
            "top CPU processes: registration",
        );
        posts.report(&mut self.post_failures, logger, "top CPU processes: post");
    }

    fn post(
        &mut self,
        usage: &Usage,
        logger: &Logger,
        registrations: &mut Tally,
        posts: &mut Tally,
    ) {
        let path = sensor_path(&usage.name);
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
                    registrations.succeeded();
                    self.sensors.insert(usage.name.clone(), sensor);
                }
                Err(error) => {
                    // Not cached: the next interval tries again.
                    registrations.failed(&path, error.to_string());
                    return;
                }
            }
        }
        let Some(sensor) = self.sensors.get(&usage.name) else {
            return;
        };
        match sensor.add(usage.percent) {
            Ok(()) => posts.succeeded(),
            Err(error) => posts.failed(&path, error.to_string()),
        }
    }
}

/// One sample's outcomes of one kind (registrations or posts).
#[derive(Debug, Default)]
struct Tally {
    attempts: usize,
    failures: usize,
    /// The first failure's path and error.
    first: Option<(String, String)>,
}

impl Tally {
    fn succeeded(&mut self) {
        self.attempts += 1;
    }

    fn failed(&mut self, path: &str, error: String) {
        self.attempts += 1;
        self.failures += 1;
        if self.first.is_none() {
            self.first = Some((path.to_string(), error));
        }
    }

    /// One line per failure episode (root rule #8 without flooding the log): the dedup key is the
    /// first error's text, which carries no sensor name, so a failure that persists across names
    /// and samples logs once; the counts and the first path ride in the logged line only. The
    /// episode ends ("recovered") only with a sample in which every attempt of this kind worked;
    /// a mixed sample keeps it open, and a sample with nothing to do decides nothing.
    fn report(self, episode: &mut Episode, logger: &Logger, what: &str) {
        let Some((path, error)) = self.first else {
            if self.attempts > 0 && episode.current.take().is_some() {
                logger.info(format!("{what}: recovered"));
            }
            return;
        };
        if episode.current.as_deref() == Some(error.as_str()) {
            return;
        }
        logger.error(format!(
            "{what}: {} of {} failed (first: {path}): {error}; repeats are not logged until a \
             sample in which all of them work",
            self.failures, self.attempts
        ));
        episode.current = Some(error);
    }
}

/// The failure currently being reported for one kind (its error text), if any.
#[derive(Debug, Default)]
struct Episode {
    current: Option<String>,
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
                let top = procfs::select_top(&by_name, self.count, self.min_percent);
                self.post_all(&top, logger);
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

    fn denied() -> std::io::Result<PathBuf> {
        Err(std::io::Error::from(std::io::ErrorKind::PermissionDenied))
    }

    /// Another user's process: `exe` is EACCES for the unprivileged probe, so argv[0] names it —
    /// only when absolute, and never with its arguments.
    #[test]
    fn another_users_process_is_named_by_its_absolute_argv0_only() {
        let tree = FakeTree::new("topcpu-argv0");
        let line = |cmdline: &[u8]| {
            std::fs::write(tree.0.join("cmdline"), cmdline).expect("write");
            path_line_from(denied(), &tree.0)
        };
        // Absolute argv[0]: shown; the arguments (a secret among them) are not.
        let shown = line(b"/usr/local/bin/node\0/srv/app/server.js\0--token=s3cr3t\0");
        assert_eq!(shown, "\n\n**Path:** `/usr/local/bin/node`");
        // A title rewritten into one space-joined string must not leak its arguments either.
        let rewritten = line(b"/opt/google/chrome/chrome --type=renderer --token=s3cr3t\0\0\0");
        assert_eq!(rewritten, "\n\n**Path:** `/opt/google/chrome/chrome`");
        for leaked in [&shown, &rewritten] {
            assert!(
                !leaked.contains("s3cr3t") && !leaked.contains("--"),
                "{leaked}"
            );
        }
        // Relative argv[0], a rewritten title, an empty cmdline (a zombie), a missing file: the
        // note.
        for cmdline in [
            &b"python3\0app.py\0"[..],
            b"postgres: checkpointer \0",
            b"",
            b"\0",
        ] {
            assert_eq!(line(cmdline), OTHER_USER_PATH, "{cmdline:?}");
        }
        std::fs::remove_file(tree.0.join("cmdline")).expect("rm");
        assert_eq!(path_line_from(denied(), &tree.0), OTHER_USER_PATH);
        // Control characters and backticks never reach the description; a long path is capped.
        assert_eq!(
            line(b"/opt/a`b\x07c/run\0x\0"),
            "\n\n**Path:** `/opt/abc/run`"
        );
        let long = format!("/{}\0", "d".repeat(1000));
        let capped = line(long.as_bytes());
        assert!(capped.ends_with("...`"), "{capped}");
        assert_eq!(
            capped.len(),
            "\n\n**Path:** ``".len() + MAX_PATH_CHARS,
            "{capped}"
        );
        // A readable exe link wins; any other error (the process exited) omits the line.
        assert_eq!(
            path_line_from(Ok(PathBuf::from("/usr/bin/yes")), &tree.0),
            "\n\n**Path:** `/usr/bin/yes`"
        );
        assert_eq!(
            path_line_from(
                Err(std::io::Error::from(std::io::ErrorKind::NotFound)),
                &tree.0
            ),
            ""
        );
    }

    fn test_source<'c>(collector: &'c Collector, root: &Path, max_tracked: usize) -> TopCpu<'c> {
        TopCpu {
            collector,
            sampler: Sampler::new(root),
            count: 10,
            min_percent: 1.0,
            period: Duration::from_secs(60),
            max_tracked,
            sensors: HashMap::new(),
            cap_logged: false,
            baseline_logged: false,
            failures: FailureLog::default(),
            registration_failures: Episode::default(),
            post_failures: Episode::default(),
        }
    }

    fn busy(name: &str, percent: f64) -> Usage {
        Usage {
            name: name.into(),
            percent,
            pid: 1,
            kernel_thread: false,
        }
    }

    /// Log lines added since `seen`, which is moved past them.
    fn new_lines(lines: &Arc<Mutex<Vec<String>>>, seen: &mut usize) -> Vec<String> {
        let lines = lines.lock().unwrap();
        let added = lines[*seen..].to_vec();
        *seen = lines.len();
        added
    }

    /// A post that keeps failing logs one ERROR per episode, not one per name and minute; only a
    /// sample with no failure ends it, so a mixed sample does not flip-flop. A non-finite value is
    /// what the collector really rejects (`hsm_sensor_add_double`: INVALID_ARGUMENT).
    #[test]
    fn a_persistent_post_failure_is_one_line_per_episode() {
        let tree = FakeTree::new("topcpu-post-fail");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = test_source(&collector, &tree.0, 64);
        collector.start().expect("start");
        let mut seen = 0;
        let failing = [
            busy("a", f64::NAN),
            busy("b", f64::NAN),
            busy("c", f64::NAN),
        ];

        source.post_all(&failing, &logger);
        let first = new_lines(&lines, &mut seen);
        assert_eq!(first.len(), 1, "{first:#?}");
        assert!(first[0].contains("|ERROR|"), "{}", first[0]);
        assert!(
            first[0].contains(
                "top CPU processes: post: 3 of 3 failed (first: .computer/Top CPU processes/a)"
            ),
            "{}",
            first[0]
        );
        source.post_all(&failing, &logger);
        assert!(
            new_lines(&lines, &mut seen).is_empty(),
            "the same failure again"
        );

        // Mixed: still failing, nothing logged, no "recovered".
        let mixed = [busy("a", f64::NAN), busy("b", 5.0), busy("c", 7.0)];
        source.post_all(&mixed, &logger);
        source.post_all(&mixed, &logger);
        assert!(
            new_lines(&lines, &mut seen).is_empty(),
            "mixed samples keep the episode open"
        );

        // A clean sample ends it once; an empty one decides nothing.
        source.post_all(&[busy("a", 3.0), busy("b", 5.0)], &logger);
        let recovered = new_lines(&lines, &mut seen);
        assert_eq!(recovered.len(), 1, "{recovered:#?}");
        assert!(recovered[0].contains("top CPU processes: post: recovered"));
        source.post_all(&[], &logger);
        source.post_all(&[busy("a", 3.0)], &logger);
        assert!(new_lines(&lines, &mut seen).is_empty());

        // A new episode logs again, once.
        source.post_all(&[busy("b", f64::INFINITY)], &logger);
        source.post_all(&[busy("c", f64::NAN)], &logger);
        assert_eq!(new_lines(&lines, &mut seen).len(), 1);
        collector.stop().expect("stop");
    }

    /// Registration failures (here: the collector is disposed) are one line too.
    #[test]
    fn a_persistent_registration_failure_is_one_line_per_episode() {
        let tree = FakeTree::new("topcpu-reg-fail");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = test_source(&collector, &tree.0, 64);
        collector.dispose();
        let mut seen = 0;
        let top = [busy("a", 5.0), busy("b", 4.0), busy("c", 3.0)];
        source.post_all(&top, &logger);
        let first = new_lines(&lines, &mut seen);
        assert_eq!(first.len(), 1, "{first:#?}");
        assert!(
            first[0].contains("top CPU processes: registration: 3 of 3 failed (first: .computer/Top CPU processes/a)"),
            "{}",
            first[0]
        );
        source.post_all(&top, &logger);
        assert!(new_lines(&lines, &mut seen).is_empty());
        assert!(
            source.sensors.is_empty(),
            "nothing is cached; the next sample tries again"
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
        let mut source = test_source(&collector, &tree.0, 2);
        collector.start().expect("start");
        for name in ["a", "b", "c", "d", "a"] {
            source.post_all(&[busy(name, 5.0)], &logger);
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
