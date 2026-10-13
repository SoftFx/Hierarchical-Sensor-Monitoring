//! Top disk I/O processes (#1547): which process writes and reads the disks the most, right now
//! and over a day.
//!
//! | Sensor | Type · unit | When |
//! |---|---|---|
//! | `.computer/Top disk writers/<name>` | Double · MBytes_sec | every `periodMs` (60 s): names at or above `minBytesPerSec`, the fastest `count` |
//! | `.computer/Top disk readers/<name>` | the same, for the reads | the same reads |
//! | `.computer/Written per day by process/<name>` | Double · GB (decimal) | once a day, in its last 30 s: the top `count` names by the day's total |
//! | `.computer/Read per day by process/<name>` | the same, for the reads | posted with it |
//!
//! # Placement
//!
//! Probe-only, like top CPU (#1479): per-process I/O is not in the shared default-sensor catalog,
//! so there is no Windows or managed counterpart and no conformance scenario (root rules #9/#10
//! apply when a sensor is shared, which this one is not).
//!
//! # Mirrors top CPU
//!
//! The same process table ([`top_cpu::procfs::read_processes`]), the same name rule
//! ([`top_cpu::procfs::sensor_name`]: `comm`, normalized; kernel threads by kind), processes of
//! one name summed, `(pid, starttime)` identity, a first read that only sets the baseline, sensors
//! registered at runtime for the names that make the top, the same name cap
//! ([`top_cpu::max_tracked_names`]) and the same path line in the speed sensors' descriptions
//! ([`top_cpu::path_line`]). Values are MB/s (1 MB = 1024² bytes), the unit of the disk speed
//! sensors (`Average disk write speed on <name> disk`).
//!
//! # Schedule
//!
//! The source ticks every 5 s (or every `periodMs` when that is shorter) and reads `/proc` only
//! when a sample is due: every `periodMs` on the tick grid ([`on_tick_grid`]), plus one **final
//! reading** in the day's last 30 seconds, after which the day's totals are posted — the window
//! `Written per day on <disk>` posts in. A tick that is not due costs a clock read.
//!
//! # Privileges
//!
//! `/proc/<pid>/io` of another user's process needs two capabilities: the file is mode 0400 and
//! owned by that user (`CAP_DAC_READ_SEARCH` opens it), and the kernel checks
//! `ptrace_may_access` before answering (`CAP_SYS_PTRACE`); either alone still reads `EACCES`
//! (measured in a systemd container, 2026-10-13). Under the unit's `ProtectProc=invisible` other
//! processes are not visible at all. The bundle's `install.sh`
//! writes [`DROP_IN`] when the host config enables `topDiskIo`. Without it the source keeps
//! measuring what it can read and says once, at WARN, what it cannot and how to fix it.
//!
//! [`top_cpu`]: super::top_cpu
//! [`top_cpu::procfs::read_processes`]: super::top_cpu::procfs::read_processes
//! [`top_cpu::procfs::sensor_name`]: super::top_cpu::procfs::sensor_name
//! [`top_cpu::max_tracked_names`]: super::top_cpu::max_tracked_names
//! [`top_cpu::path_line`]: super::top_cpu::path_line

pub mod day;
pub mod procfs;

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use hsm_collector::{Collector, DoubleSensor, SensorOptions, SensorStatus};

use self::day::DayLedger;
use self::procfs::{Direction, IoRead, IoSampler, NameIo};
use super::disks::written::{day_label, local_time, LocalTime};
use super::top_cpu::procfs::hidepid_option;
use super::top_cpu::{max_tracked_names, path_line, Episode, Tally};
use super::{on_tick_grid, FailureLog, Source};
use crate::config::TopDiskIoConfig;
use crate::logging::{Level, Logger};

pub const WRITERS: &str = ".computer/Top disk writers";
pub const READERS: &str = ".computer/Top disk readers";
pub const WRITTEN_PER_DAY: &str = ".computer/Written per day by process";
pub const READ_PER_DAY: &str = ".computer/Read per day by process";

/// The unit drop-in that lets the probe read every process's `io`, written by the bundle's
/// `install.sh` when the host config enables `topDiskIo`.
pub const DROP_IN: &str = "/etc/systemd/system/hsm-linux-probe.service.d/top-disk-io.conf";

/// Codes of the managed `Unit` enum.
const UNIT_MBYTES_SEC: i32 = 2103;
const UNIT_GB: i32 = 4;
/// MB as the disk speed sensors count it.
const BYTES_PER_MB: f64 = 1024.0 * 1024.0;
/// The speed sensors' TTL: the top-CPU 5 minutes, or three periods when that is longer — a name
/// that stops being posted (idle, exited) turns to Timeout.
const SPEED_TTL: Duration = Duration::from_secs(5 * 60);
/// The per-day sensors' TTL, as `Written per day on <disk>`: one missed day shows as Timeout.
const PER_DAY_TTL: Duration = Duration::from_secs(26 * 3600);
/// The tick: the final reading lands in a 30-s window, which six 5-s ticks always hit.
const TICK: Duration = Duration::from_secs(5);
/// The day's totals are posted in its last this many seconds.
const FINAL_WINDOW_SECONDS: i64 = 30;

const WHAT: &str = "top disk I/O";

/// Register the source when `topDiskIo.enabled`. No sensor exists before the first measured
/// interval: each name registers when it first makes a top (at runtime).
pub fn register<'c>(
    collector: &'c Collector,
    config: &TopDiskIoConfig,
    proc_root: &Path,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    if !config.enabled {
        logger.info("top disk I/O: off (topDiskIo.enabled = false)");
        return None;
    }
    let source = TopDiskIo::new(collector, config, proc_root);
    logger.info(format!(
        "top disk I/O: every {} s, the {} fastest process names at or above {} under \
         {WRITERS}/<name> and {READERS}/<name>; the day's top {} under {WRITTEN_PER_DAY}/<name> \
         and {READ_PER_DAY}/<name>, posted in the day's last {FINAL_WINDOW_SECONDS} s",
        source.sample_period.as_secs_f64(),
        source.count,
        speed_label(source.min_bytes_per_sec),
        source.count
    ));
    // Degrades, never fails: with hidepid only the probe's own processes are visible.
    let mountinfo = std::fs::read_to_string(proc_root.join("self/mountinfo")).unwrap_or_default();
    if let Some(option) = hidepid_option(&mountinfo, &proc_root.to_string_lossy()) {
        logger.warn(format!(
            "top disk I/O: {} is mounted with {option} (systemd ProtectProc=), so only this \
             service's own processes are visible and the sensors cannot show the host's disk I/O; \
             the drop-in {DROP_IN} (ProtectProc=default, CAP_DAC_READ_SEARCH and \
             CAP_SYS_PTRACE) lifts it: the bundle's \
             install.sh writes it when the top-level topDiskIo block is enabled (README, Top disk \
             I/O processes)",
            proc_root.display()
        ));
    }
    Some(Box::new(source))
}

/// One posted value.
struct Post {
    name: String,
    value: f64,
    /// The representative process (pid, kernel thread) whose executable the description names.
    process: Option<(u32, bool)>,
}

/// One of the four sensor families: a sensor per name, created when the name is first posted and
/// kept for the probe's lifetime, at most `max_tracked` of them (the server has no sensor delete).
struct Family<'c> {
    category: &'static str,
    max_tracked: usize,
    sensors: HashMap<String, DoubleSensor<'c>>,
    cap_logged: bool,
    registration_failures: Episode,
    post_failures: Episode,
    /// (path, value, comment) of every value the collector accepted.
    #[cfg(test)]
    sent: Vec<(String, f64, Option<String>)>,
}

impl<'c> Family<'c> {
    fn new(category: &'static str, max_tracked: usize) -> Self {
        Self {
            category,
            max_tracked,
            sensors: HashMap::new(),
            cap_logged: false,
            registration_failures: Episode::default(),
            post_failures: Episode::default(),
            #[cfg(test)]
            sent: Vec::new(),
        }
    }

    fn path(&self, name: &str) -> String {
        format!("{}/{name}", self.category)
    }

    /// Register the new names and post every value. Failures are logged once per episode per
    /// kind (the top-CPU [`Tally`]). Returns (values offered to the collector, values accepted).
    fn post_all(
        &mut self,
        collector: &'c Collector,
        posts: &[Post],
        comment: Option<&str>,
        options: &dyn Fn(&Post) -> SensorOptions,
        logger: &Logger,
    ) -> (usize, usize) {
        let (mut registrations, mut values) = (Tally::default(), Tally::default());
        let (mut attempted, mut sent) = (0, 0);
        for post in posts {
            let path = self.path(&post.name);
            if !self.sensors.contains_key(&post.name) {
                if self.sensors.len() >= self.max_tracked {
                    if !self.cap_logged {
                        logger.warn(format!(
                            "{WHAT}: {} distinct names have a sensor under {}, the cap; new names \
                             get none (the tracked ones keep reporting)",
                            self.max_tracked, self.category
                        ));
                        self.cap_logged = true;
                    }
                    continue;
                }
                attempted += 1;
                match collector.double_sensor(&path, &options(post)) {
                    Ok(sensor) => {
                        registrations.succeeded();
                        self.sensors.insert(post.name.clone(), sensor);
                    }
                    Err(error) => {
                        // Not cached: the next post tries again.
                        registrations.failed(&path, error.to_string());
                        continue;
                    }
                }
            } else {
                attempted += 1;
            }
            let Some(sensor) = self.sensors.get(&post.name) else {
                continue;
            };
            let result = match comment {
                Some(comment) => sensor.add_with(post.value, SensorStatus::Ok, Some(comment)),
                None => sensor.add(post.value),
            };
            match result {
                Ok(()) => {
                    values.succeeded();
                    sent += 1;
                    #[cfg(test)]
                    self.sent
                        .push((path, post.value, comment.map(str::to_string)));
                }
                Err(error) => values.failed(&path, error.to_string()),
            }
        }
        registrations.report(
            &mut self.registration_failures,
            logger,
            &format!("{WHAT}: {}: registration", self.category),
        );
        values.report(
            &mut self.post_failures,
            logger,
            &format!("{WHAT}: {}: post", self.category),
        );
        (attempted, sent)
    }
}

struct TopDiskIo<'c> {
    collector: &'c Collector,
    sampler: IoSampler,
    count: usize,
    min_bytes_per_sec: f64,
    sample_period: Duration,
    tick_period: Duration,
    /// When the next regular sample is due; `None`: the next tick samples (right after Start).
    next_sample: Option<Instant>,
    /// The wall clock of the previous successful read, which the day's accounting measures from.
    last_read_ms: Option<i64>,
    /// Bytes per name since the last speed post, and since when (the speeds' interval). A final
    /// reading in between adds to it, so its bytes reach the next speed post too.
    pending: BTreeMap<String, NameIo>,
    pending_since: Option<Instant>,
    ledger: DayLedger,
    writers: Family<'c>,
    readers: Family<'c>,
    written_per_day: Family<'c>,
    read_per_day: Family<'c>,
    /// Unix time in ms; injectable for tests.
    clock: fn() -> i64,
    local: LocalTime,
    baseline_logged: bool,
    denied_logged: bool,
    /// The day whose name cap was logged: once per day, so a later day is not silent.
    day_cap_logged: Option<i64>,
    failures: FailureLog,
    accounting: FailureLog,
}

impl<'c> TopDiskIo<'c> {
    fn new(collector: &'c Collector, config: &TopDiskIoConfig, proc_root: &Path) -> Self {
        Self::with_sampler(collector, config, IoSampler::new(proc_root))
    }

    fn with_sampler(
        collector: &'c Collector,
        config: &TopDiskIoConfig,
        sampler: IoSampler,
    ) -> Self {
        let (count, period) = (config.count(), config.period());
        let max_tracked = max_tracked_names(count);
        Self {
            collector,
            sampler,
            count,
            min_bytes_per_sec: config.min_bytes_per_sec,
            sample_period: period,
            tick_period: TICK.min(period),
            next_sample: None,
            last_read_ms: None,
            pending: BTreeMap::new(),
            pending_since: None,
            ledger: DayLedger::default(),
            writers: Family::new(WRITERS, max_tracked),
            readers: Family::new(READERS, max_tracked),
            written_per_day: Family::new(WRITTEN_PER_DAY, max_tracked),
            read_per_day: Family::new(READ_PER_DAY, max_tracked),
            clock: unix_now_ms,
            local: local_time,
            baseline_logged: false,
            denied_logged: false,
            day_cap_logged: None,
            failures: FailureLog::default(),
            accounting: FailureLog::default(),
        }
    }

    fn proc_root(&self) -> PathBuf {
        self.sampler.proc_root().to_path_buf()
    }

    /// One tick at `now` (monotonic) / `now_ms` (wall clock): a regular sample when due, a final
    /// reading in the day's last seconds until the day is posted, nothing otherwise.
    fn tick(&mut self, now: Instant, now_ms: i64, logger: &Logger) {
        let (day, second) = (self.local)(now_ms);
        let regular = self.next_sample.is_none_or(|due| now >= due);
        let window = second + FINAL_WINDOW_SECONDS >= 86_400
            && self.ledger.posted_day.is_none_or(|posted| posted < day);
        if !regular && !window {
            return;
        }
        if regular {
            self.next_sample = Some(on_tick_grid(now, self.sample_period, self.tick_period));
        }
        let read = match self.sampler.read() {
            Ok(read) => {
                self.failures.succeeded(logger, WHAT);
                read
            }
            // The sampler keeps its baseline: the next read measures the longer interval.
            Err(reason) => {
                self.failures.failed(logger, WHAT, &reason);
                return;
            }
        };
        self.report_visibility(&read, logger);
        for note in &read.notes {
            logger.log(Level::Debug, &format!("{WHAT}: {note}"));
        }
        let previous_ms = self.last_read_ms.replace(now_ms);
        match previous_ms.filter(|_| !read.baseline) {
            None => self.pending_since = Some(now),
            Some(previous_ms) => {
                self.count_day(&read.deltas, previous_ms, now_ms, logger);
                for (name, io) in &read.deltas {
                    self.pending.entry(name.clone()).or_default().merge(io);
                }
                if regular {
                    self.post_speeds(now, logger);
                }
            }
        }
        if window {
            self.post_day(day, now_ms, logger);
        }
    }

    fn count_day(
        &mut self,
        deltas: &BTreeMap<String, NameIo>,
        previous_ms: i64,
        now_ms: i64,
        logger: &Logger,
    ) {
        match self
            .ledger
            .add(deltas, previous_ms, now_ms, self.local, self.sample_period)
        {
            Ok(0) => {}
            Ok(dropped) => {
                if self.day_cap_logged != self.ledger.day {
                    logger.warn(format!(
                        "{WHAT}: the day already counts {} process names, the cap; {dropped} new \
                         name(s) are not counted towards the per-day totals (logged once a day)",
                        day::MAX_DAY_NAMES
                    ));
                    self.day_cap_logged = self.ledger.day;
                }
            }
            Err(skip) => logger.log(
                Level::Debug,
                &format!("{WHAT}: an interval is not counted towards the day ({skip:?})"),
            ),
        }
        for missed in std::mem::take(&mut self.ledger.missed) {
            logger.warn(format!(
                "{WHAT}: the per-day totals of {} are not posted: no sample landed in the day's \
                 last {FINAL_WINDOW_SECONDS} s (a suspended host or a stalled source); they are \
                 never posted late",
                day_label(missed)
            ));
        }
    }

    /// Once, at WARN, what cannot be read and the remedy; once, as an episode, a table with no
    /// readable `io` at all.
    fn report_visibility(&mut self, read: &IoRead, logger: &Logger) {
        if read.baseline && !self.baseline_logged {
            logger.info(format!(
                "{WHAT}: baseline over {} visible processes, {} with readable I/O counters",
                read.processes, read.readable
            ));
            self.baseline_logged = true;
        }
        if read.denied > 0 && !self.denied_logged {
            logger.warn(format!(
                "{WHAT}: /proc/<pid>/io of {} of {} processes is not readable (EACCES: another \
                 user's process needs CAP_DAC_READ_SEARCH and CAP_SYS_PTRACE, which the unit does \
                 not grant); their disk I/O is not counted, the others' is. The drop-in {DROP_IN} \
                 grants them (ProtectProc=default, CapabilityBoundingSet= and \
                 AmbientCapabilities= CAP_DAC_READ_SEARCH CAP_SYS_PTRACE): the bundle's install.sh \
                 writes it when the top-level topDiskIo block in the host config is enabled; then \
                 systemctl daemon-reload && systemctl restart hsm-linux-probe (README, Top disk \
                 I/O processes). Logged once.",
                read.denied, read.processes
            ));
            self.denied_logged = true;
        }
        if read.processes > 0 && read.readable == 0 && read.denied == 0 {
            self.accounting.failed(
                logger,
                WHAT,
                "no /proc/<pid>/io can be read, not even the probe's own (a kernel without task \
                 I/O accounting, CONFIG_TASK_IO_ACCOUNTING?)",
            );
        } else if read.readable > 0 {
            self.accounting.succeeded(logger, WHAT);
        }
    }

    /// Post the speeds over the interval since the previous speed post.
    fn post_speeds(&mut self, now: Instant, logger: &Logger) {
        let Some(since) = self.pending_since.replace(now) else {
            return;
        };
        let seconds = now.saturating_duration_since(since).as_secs_f64();
        let pending = std::mem::take(&mut self.pending);
        let proc_root = self.proc_root();
        let (count, period, min) = (self.count, self.sample_period, self.min_bytes_per_sec);
        for direction in [Direction::Write, Direction::Read] {
            let posts: Vec<Post> = procfs::select_top(&pending, direction, seconds, min, count)
                .into_iter()
                .map(|rate| Post {
                    value: megabytes_per_second(rate.bytes_per_sec),
                    name: rate.name,
                    process: Some((rate.pid, rate.kernel_thread)),
                })
                .collect();
            let options = |post: &Post| {
                let line = post
                    .process
                    .map(|(pid, kernel_thread)| path_line(&proc_root, pid, kernel_thread))
                    .unwrap_or_default();
                speed_options(direction, count, period, min, &line)
            };
            let family = match direction {
                Direction::Write => &mut self.writers,
                Direction::Read => &mut self.readers,
            };
            family.post_all(self.collector, &posts, None, &options, logger);
        }
    }

    /// Post the day's totals (its only post). The day counts as posted when something went out or
    /// there was nothing to post; when every post failed, the next tick in the window tries again.
    fn post_day(&mut self, day: i64, now_ms: i64, logger: &Logger) {
        let (count, period) = (self.count, self.sample_period);
        let (mut attempted, mut sent) = (0, 0);
        for direction in [Direction::Write, Direction::Read] {
            let Some(day::DayTop {
                names: top,
                comment,
            }) = self.ledger.top(direction, count, now_ms, self.local)
            else {
                continue;
            };
            let posts: Vec<Post> = top
                .into_iter()
                .map(|(name, gigabytes)| Post {
                    name,
                    value: gigabytes,
                    process: None,
                })
                .collect();
            let options = |_: &Post| per_day_options(direction, count, period);
            let family = match direction {
                Direction::Write => &mut self.written_per_day,
                Direction::Read => &mut self.read_per_day,
            };
            let (offered, accepted) =
                family.post_all(self.collector, &posts, comment.as_deref(), &options, logger);
            attempted += offered;
            sent += accepted;
        }
        if attempted == 0 || sent > 0 {
            self.ledger.posted_day = Some(day);
            logger.log(
                Level::Debug,
                &format!(
                    "{WHAT}: per-day totals of {}: {sent} of {attempted} value(s) posted",
                    day_label(day)
                ),
            );
        }
    }
}

impl Source for TopDiskIo<'_> {
    fn name(&self) -> &'static str {
        "top disk io"
    }

    fn period(&self) -> Duration {
        self.tick_period
    }

    fn sample(&mut self, logger: &Logger) {
        self.tick(Instant::now(), (self.clock)(), logger);
    }
}

fn megabytes_per_second(bytes_per_sec: f64) -> f64 {
    (bytes_per_sec / BYTES_PER_MB * 1000.0).round() / 1000.0
}

/// `1 MB/s` for 1048576 bytes per second.
fn speed_label(bytes_per_sec: f64) -> String {
    format!("{} MB/s", megabytes_per_second(bytes_per_sec))
}

fn speed_options(
    direction: Direction,
    count: usize,
    period: Duration,
    min_bytes_per_sec: f64,
    path_line: &str,
) -> SensorOptions {
    let (who, field) = match direction {
        Direction::Write => ("writers", "write_bytes"),
        Direction::Read => ("readers", "read_bytes"),
    };
    SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_ttl(SPEED_TTL.max(period.saturating_mul(3)))
        .with_unit(UNIT_MBYTES_SEC)
        .with_enable_grafana(true)
        .with_description(format!(
            "Top **{count}** disk {who} by MB/s (1 MB = 1024² bytes): `{field}` of \
             /proc/<pid>/io — block I/O, not page-cache hits or writes that never reach a disk — \
             summed over every process of this name, averaged over {} s. Posted only while at \
             least {} and among the top {count}.{path_line}",
            period.as_secs_f64(),
            speed_label(min_bytes_per_sec)
        ))
}

fn per_day_options(direction: Direction, count: usize, period: Duration) -> SensorOptions {
    let (what, field, moved) = match direction {
        Direction::Write => ("Written to", "write_bytes", "written"),
        Direction::Read => ("Read from", "read_bytes", "read"),
    };
    SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_ttl(PER_DAY_TTL)
        .with_unit(UNIT_GB)
        .with_description(format!(
            "{what} the disks by the processes of this name during one local day (the host's \
             timezone), in GB (decimal: 1 GB = 10⁹ bytes): `{field}` of /proc/<pid>/io, summed \
             from the {}-s samples. **One value per day**, posted in the day's last 30 seconds \
             for the top **{count}** names by the day's total; what is {moved} between a post and \
             midnight counts towards the next day. A process that exits between two samples has \
             its last partial interval counted under the process that reaped it (the kernel adds \
             a reaped child's counters to its parent's). A day with no measurement is not posted \
             (never an invented 0); a day whose measurement began after midnight — the probe \
             started or restarted during it — says since when in the comment. TTL 26 h: a \
             missing day shows as Timeout.",
            period.as_secs_f64()
        ))
}

fn unix_now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::procfs::tests::{gone, process, refusing_reader, DENIED};
    use super::*;
    use crate::logging::Level;
    use crate::probe_only::disks::written::tests::{utc, MIDNIGHT};
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;
    use std::sync::{Arc, Mutex};

    const MB: u64 = 1_048_576;
    const MIN_MS: i64 = 60_000;

    fn enabled() -> TopDiskIoConfig {
        TopDiskIoConfig {
            enabled: true,
            ..TopDiskIoConfig::default()
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

    fn registrations(collector: &Collector, category: &str) -> Vec<String> {
        collector
            .registrations()
            .into_iter()
            .filter(|json| json.contains(&format!("\"Path\":\"{category}/")))
            .collect()
    }

    /// The source on a UTC "local" calendar, reading `tree`.
    fn source<'c>(collector: &'c Collector, tree: &FakeTree) -> TopDiskIo<'c> {
        let mut source = TopDiskIo::with_sampler(
            collector,
            &enabled(),
            IoSampler::with_reader(&tree.0, refusing_reader),
        );
        source.local = utc;
        source
    }

    fn sent(family: &Family<'_>) -> Vec<(String, f64)> {
        family
            .sent
            .iter()
            .map(|(path, value, _)| (path.clone(), *value))
            .collect()
    }

    #[test]
    fn off_unless_enabled() {
        let collector = test_collector();
        let (logger, lines) = capture();
        let tree = FakeTree::new("topio-off");
        assert!(register(&collector, &TopDiskIoConfig::default(), &tree.0, &logger).is_none());
        assert!(lines.lock().unwrap().iter().any(|l| l.contains("off")));
    }

    #[test]
    fn the_speeds_register_at_runtime_for_the_top_names_only() {
        let tree = FakeTree::new("topio-wire");
        process(&tree, 100, "postgres", 1, 50, 0, 0);
        process(&tree, 101, "postgres", 1, 51, 0, 0);
        process(&tree, 200, "quiet", 1, 60, 0, 0);
        let collector = test_collector();
        let (logger, _) = capture();
        let registered = register(&collector, &enabled(), &tree.0, &logger).expect("source");
        assert_eq!(registered.period(), TICK);
        drop(registered);
        let mut source = source(&collector, &tree);
        collector.start().expect("start");

        let start = Instant::now();
        let noon = MIDNIGHT + 12 * 3600 * 1000;
        source.tick(start, noon, &logger);
        assert!(
            registrations(&collector, WRITERS).is_empty(),
            "the baseline"
        );

        // Not due 5 s later: no read at all.
        process(&tree, 100, "postgres", 1, 50, 0, 600 * MB);
        source.tick(start + TICK, noon + 5_000, &logger);
        assert!(source.writers.sent.is_empty());

        // 60 s: postgres wrote 90 MB (1.5 MB/s), read 120 MB; "quiet" 59 MB is under 1 MB/s.
        process(&tree, 100, "postgres", 1, 50, 120 * MB, 60 * MB);
        process(&tree, 101, "postgres", 1, 51, 0, 30 * MB);
        process(&tree, 200, "quiet", 1, 60, 0, 59 * MB);
        source.tick(start + Duration::from_secs(60), noon + MIN_MS, &logger);
        assert_eq!(
            sent(&source.writers),
            [(format!("{WRITERS}/postgres"), 1.5)]
        );
        assert_eq!(
            sent(&source.readers),
            [(format!("{READERS}/postgres"), 2.0)]
        );
        let writers = registrations(&collector, WRITERS);
        collector.stop().expect("stop");
        assert_eq!(writers.len(), 1, "{writers:#?}");
        for part in [
            "\"SensorType\":2,",
            "\"OriginalUnit\":2103,",
            "\"TTLTicks\":[3000000000]",
            "\"EnableGrafana\":true",
            "Top **10** disk writers by MB/s",
            "`write_bytes` of /proc/<pid>/io",
            "at least 1 MB/s",
        ] {
            assert!(
                writers[0].contains(part),
                "missing {part} in {}",
                writers[0]
            );
        }
    }

    /// The day: counted from every read, posted once in its last 30 s from a final reading, then
    /// the next day starts — and a process that exits between two samples is credited to its
    /// reaper for its last partial interval, never counted twice.
    #[test]
    fn the_day_is_posted_once_in_its_last_seconds_and_rolls_over() {
        let tree = FakeTree::new("topio-day");
        process(&tree, 10, "bash", 1, 5, 0, 0);
        process(&tree, 11, "dd", 10, 6, 0, 0);
        process(&tree, 20, "rsync", 1, 7, 0, 0);
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let day = utc(MIDNIGHT).0;
        let t0 = Instant::now();
        let at = |seconds: u64| t0 + Duration::from_secs(seconds);
        // 23:57:00 baseline, 23:58:00 and 23:59:00 regular, 23:59:35 the final reading.
        let base_ms = MIDNIGHT + 24 * 3600 * 1000 - 3 * MIN_MS;
        source.tick(at(0), base_ms, &logger);

        process(&tree, 11, "dd", 10, 6, 0, 2_000 * MB);
        process(&tree, 20, "rsync", 1, 7, 500 * MB, 100 * MB);
        source.tick(at(60), base_ms + MIN_MS, &logger);
        process(&tree, 11, "dd", 10, 6, 0, 3_000 * MB);
        source.tick(at(120), base_ms + 2 * MIN_MS, &logger);
        assert!(
            source.written_per_day.sent.is_empty(),
            "not in the window yet"
        );

        // dd wrote 500 MB more, exited and was reaped by bash: bash's counter jumps by dd's whole
        // 3 500 MB; bash itself wrote 10 MB.
        gone(&tree, 11);
        process(&tree, 10, "bash", 1, 5, 0, 10 * MB + 3_500 * MB);
        source.tick(at(155), base_ms + 2 * MIN_MS + 35_000, &logger);
        let written = sent(&source.written_per_day);
        assert_eq!(
            written,
            [
                (format!("{WRITTEN_PER_DAY}/dd"), 3.146),
                (format!("{WRITTEN_PER_DAY}/bash"), 0.535),
                (format!("{WRITTEN_PER_DAY}/rsync"), 0.105),
            ],
            "dd's 3000 MB, bash's own 10 MB + dd's last 500 MB, rsync's 100 MB"
        );
        assert_eq!(
            sent(&source.read_per_day),
            [(format!("{READ_PER_DAY}/rsync"), 0.524)]
        );
        assert_eq!(source.ledger.posted_day, Some(day));
        let per_day = registrations(&collector, WRITTEN_PER_DAY);
        for part in [
            "\"OriginalUnit\":4,",
            "\"TTLTicks\":[936000000000]",
            "**One value per day**",
        ] {
            assert!(
                per_day[0].contains(part),
                "missing {part} in {}",
                per_day[0]
            );
        }

        // Later ticks in the window post nothing more; what is written after the post counts
        // towards the next day.
        process(&tree, 20, "rsync", 1, 7, 500 * MB, 100 * MB + 2_000 * MB);
        source.tick(at(160), base_ms + 2 * MIN_MS + 40_000, &logger);
        source.tick(at(180), base_ms + 3 * MIN_MS, &logger);
        assert_eq!(source.written_per_day.sent.len(), 3);
        assert_eq!(source.ledger.day, Some(day + 1));
        assert_eq!(source.ledger.names["rsync"].written, 2_000 * MB);
        assert!(!lines
            .lock()
            .unwrap()
            .iter()
            .any(|l| l.contains("|WARN|") || l.contains("|ERROR|")));
        collector.stop().expect("stop");
    }

    #[test]
    fn a_missed_window_is_logged_once_and_never_posted_late() {
        let tree = FakeTree::new("topio-missed");
        process(&tree, 20, "rsync", 1, 7, 0, 0);
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let t0 = Instant::now();
        let evening = MIDNIGHT + 24 * 3600 * 1000 - 2 * MIN_MS;
        source.tick(t0, evening, &logger);
        process(&tree, 20, "rsync", 1, 7, 0, 100 * MB);
        source.tick(t0 + Duration::from_secs(60), evening + MIN_MS, &logger);
        // The tick in the window never came: the next one is after midnight.
        process(&tree, 20, "rsync", 1, 7, 0, 200 * MB);
        source.tick(t0 + Duration::from_secs(150), evening + 150_000, &logger);
        assert!(source.written_per_day.sent.is_empty());
        let warned: Vec<String> = lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains("are not posted"))
            .cloned()
            .collect();
        assert_eq!(warned.len(), 1, "{warned:#?}");
        assert!(warned[0].contains("|WARN|") && warned[0].contains("2026-09-29"));
        collector.stop().expect("stop");
    }

    /// Without the capabilities another user's `io` is refused: one WARN line naming the remedy,
    /// and the readable processes keep being measured.
    #[test]
    fn a_refused_io_is_one_warning_with_the_remedy_and_sampling_goes_on() {
        let tree = FakeTree::new("topio-denied");
        process(&tree, 1, "systemd", 0, 1, 0, 0);
        tree.file("1/io", DENIED);
        process(&tree, 50, "mine", 1, 5, 0, 0);
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let t0 = Instant::now();
        let noon = MIDNIGHT + 12 * 3600 * 1000;
        source.tick(t0, noon, &logger);
        for minute in 1..=3 {
            process(&tree, 50, "mine", 1, 5, 0, minute * 120 * MB);
            source.tick(
                t0 + Duration::from_secs(60 * minute),
                noon + MIN_MS * minute as i64,
                &logger,
            );
        }
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        let warnings: Vec<&String> = lines.iter().filter(|l| l.contains("|WARN|")).collect();
        assert_eq!(warnings.len(), 1, "{lines:#?}");
        for part in [
            "of 1 of 2 processes is not readable (EACCES",
            "needs CAP_DAC_READ_SEARCH and CAP_SYS_PTRACE",
            DROP_IN,
            "install.sh",
        ] {
            assert!(
                warnings[0].contains(part),
                "missing {part} in {}",
                warnings[0]
            );
        }
        assert_eq!(
            source.writers.sent.len(),
            3,
            "the readable one keeps posting"
        );
        assert!(!lines.iter().any(|l| l.contains("|ERROR|")), "{lines:#?}");
    }

    #[test]
    fn hidepid_is_one_warning_and_no_io_at_all_is_an_error_episode() {
        let tree = FakeTree::new("topio-hidepid");
        let proc_root = tree.0.to_string_lossy().to_string();
        tree.file(
            "self/mountinfo",
            &format!("812 22 0:77 / {proc_root} rw,nosuid - proc proc rw,hidepid=invisible\n"),
        );
        // A kernel without task I/O accounting: no io file anywhere.
        process(&tree, 50, "mine", 1, 5, 0, 0);
        std::fs::remove_file(tree.0.join("50/io")).expect("rm");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = register(&collector, &enabled(), &tree.0, &logger).expect("source");
        source.sample(&logger);
        source.sample(&logger);
        let lines = lines.lock().unwrap();
        let hidepid: Vec<&String> = lines.iter().filter(|l| l.contains("hidepid")).collect();
        assert_eq!(hidepid.len(), 1, "{lines:#?}");
        assert!(hidepid[0].contains("|WARN|") && hidepid[0].contains(DROP_IN));
        let errors: Vec<&String> = lines.iter().filter(|l| l.contains("|ERROR|")).collect();
        assert_eq!(errors.len(), 1, "{lines:#?}");
        assert!(errors[0].contains("CONFIG_TASK_IO_ACCOUNTING"));
    }

    #[test]
    fn an_unreadable_proc_is_logged_once_and_posts_nothing() {
        let tree = FakeTree::new("topio-broken");
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
        assert!(registrations(&collector, WRITERS).is_empty());
    }

    /// The per-day name cap is reported once a day, not once per probe run.
    #[test]
    fn the_day_name_cap_is_logged_once_a_day() {
        let tree = FakeTree::new("topio-daycap");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        let many: BTreeMap<String, NameIo> = (0..day::MAX_DAY_NAMES + 1)
            .map(|i| {
                (
                    format!("p{i:05}"),
                    NameIo {
                        written: MB,
                        ..NameIo::default()
                    },
                )
            })
            .collect();
        let noon = MIDNIGHT + 12 * 3600 * 1000;
        for day in 0..2 {
            let at = noon + day * 24 * 3600 * 1000;
            source.count_day(&many, at, at + MIN_MS, &logger);
            source.count_day(&many, at + MIN_MS, at + 2 * MIN_MS, &logger);
        }
        let warned = lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains("the cap"))
            .count();
        assert_eq!(warned, 2, "one per day");
    }

    #[test]
    fn new_names_stop_at_the_cap() {
        let tree = FakeTree::new("topio-cap");
        let collector = test_collector();
        let (logger, lines) = capture();
        collector.start().expect("start");
        let mut family = Family::new(WRITERS, 2);
        let options = |_: &Post| SensorOptions::default();
        for name in ["a", "b", "c", "a"] {
            let post = Post {
                name: name.into(),
                value: 1.0,
                process: None,
            };
            family.post_all(&collector, &[post], None, &options, &logger);
        }
        collector.stop().expect("stop");
        drop(tree);
        assert_eq!(family.sensors.len(), 2);
        assert_eq!(family.sent.len(), 3, "a, b and a again");
        let warned = lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains("the cap"))
            .count();
        assert_eq!(warned, 1);
    }
}
