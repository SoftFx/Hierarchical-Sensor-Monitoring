//! Top disk I/O processes (#1547): which process writes and reads the disks the most, right now
//! and over a day.
//!
//! | Sensor | Type · unit | When |
//! |---|---|---|
//! | `.computer/Top disk writers/<name>` | Double · MBytes_sec | every `periodMs` (60 s): names at or above `minBytesPerSec`, the fastest `count` |
//! | `.computer/Top disk readers/<name>` | the same, for the reads | the same snapshots |
//! | `.computer/Written per day by process/<name>` | Double · GB (decimal) | once a day, in its last 30 s: the top `count` names by the day's total |
//! | `.computer/Read per day by process/<name>` | the same, for the reads | posted with it |
//!
//! # Placement
//!
//! Probe-only, like top CPU (#1479): per-process I/O is not in the shared default-sensor catalog,
//! so there is no Windows or managed counterpart and no conformance scenario (root rules #9/#10
//! apply when a sensor is shared, which this one is not).
//!
//! # Two processes: the unprivileged probe and a root helper
//!
//! Another user's `/proc/<pid>/io` needs `CAP_DAC_READ_SEARCH` (the file is 0400) and
//! `CAP_SYS_PTRACE` (the kernel's ptrace-access check) — and a process holding both can read the
//! whole host filesystem through `/proc/<pid>/root`. So the probe holds neither: the root-run
//! helper `hsm-linux-probe-io` (crate `hsm-linux-probe-io`, unit `hsm-linux-probe-io.service`)
//! reads every process's `stat` and `io` every `periodMs`, plus once in the day's last 30 seconds,
//! and replaces [`SNAPSHOT`] atomically; this source reads that file. `install.sh` enables the
//! helper when the host config enables `topDiskIo`.
//!
//! # Mirrors top CPU
//!
//! The same process table (the helper uses the stat parser top CPU uses), the same name rule
//! ([`top_cpu::procfs::sensor_name`]: `comm`, normalized; kernel threads by kind), processes of
//! one name summed, `(pid, starttime)` identity, a first snapshot that only sets the baseline,
//! sensors registered at runtime for the names that make the top, the same name cap
//! ([`top_cpu::max_tracked_names`]) and the same path line in the speed sensors' descriptions
//! ([`top_cpu::path_line`], as far as the unprivileged probe can see the process). Values are
//! MB/s (1 MB = 1024² bytes), the unit of the disk speed sensors.
//!
//! # Schedule
//!
//! The source ticks every 5 s and consumes each new snapshot once (the helper owns the cadence): a
//! `regular` one posts the speeds over the interval since the previous regular one (the helper's
//! monotonic timestamps), and a snapshot in the day's last 30 seconds posts the day's totals. A
//! snapshot that is missing, unreadable or older than three periods — measured from the last good
//! one, so a helper restart is no outage — is one WARN per outage naming the helper's unit, and
//! nothing is posted; the first good one after it is one INFO.
//!
//! [`top_cpu::procfs::sensor_name`]: super::top_cpu::procfs::sensor_name
//! [`top_cpu::max_tracked_names`]: super::top_cpu::max_tracked_names
//! [`top_cpu::path_line`]: super::top_cpu::path_line

pub mod day;
pub mod sampler;

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, PathBuf};
use std::time::Duration;

use hsm_collector::{Collector, DoubleSensor, SensorOptions, SensorStatus};
use hsm_linux_probe_io::clock::monotonic_ns;
use hsm_linux_probe_io::snapshot::{Kind, Snapshot};

use self::day::DayLedger;
use self::sampler::{Direction, IoRead, IoSampler, NameIo};
use super::disks::written::{day_label, local_time, LocalTime};
use super::top_cpu::{max_tracked_names, path_line, Episode, Tally, PROC_ROOT};
use super::{FailureLog, Source};
use crate::config::TopDiskIoConfig;
use crate::logging::{Level, Logger};

pub const WRITERS: &str = ".computer/Top disk writers";
pub const READERS: &str = ".computer/Top disk readers";
pub const WRITTEN_PER_DAY: &str = ".computer/Written per day by process";
pub const READ_PER_DAY: &str = ".computer/Read per day by process";

/// The helper's snapshot (`hsm_linux_probe_io::snapshot`).
pub const SNAPSHOT: &str = hsm_linux_probe_io::snapshot::DEFAULT_PATH;
/// The helper's unit.
pub const HELPER_UNIT: &str = "hsm-linux-probe-io.service";

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
/// How often the snapshot is looked at: a new one is consumed within 5 s.
const TICK: Duration = Duration::from_secs(5);
/// The day's totals are posted from a snapshot taken in its last this many seconds.
const FINAL_WINDOW_SECONDS: i64 = 30;
/// A snapshot older than this many periods is stale (the helper stopped).
const STALE_PERIODS: u32 = 3;

const WHAT: &str = "top disk I/O";

/// Register the source when `topDiskIo.enabled`. No sensor exists before the first measured
/// interval: each name registers when it first makes a top (at runtime).
pub fn register<'c>(
    collector: &'c Collector,
    config: &TopDiskIoConfig,
    snapshot: &Path,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    if !config.enabled {
        // Opt-in and not advertised: the default start log does not name it.
        logger.log(
            Level::Debug,
            "top disk I/O: off (topDiskIo.enabled = false)",
        );
        return None;
    }
    let source = TopDiskIo::new(collector, config, snapshot);
    logger.info(format!(
        "top disk I/O: from {} ({HELPER_UNIT}, every {} s): the {} fastest process names at or \
         above {} under {WRITERS}/<name> and {READERS}/<name>; the day's top {} under \
         {WRITTEN_PER_DAY}/<name> and {READ_PER_DAY}/<name>, posted in the day's last \
         {FINAL_WINDOW_SECONDS} s",
        snapshot.display(),
        source.sample_period.as_secs_f64(),
        source.count,
        speed_label(source.min_bytes_per_sec),
        source.count
    ));
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
    snapshot_path: PathBuf,
    /// Where the path lines of the speed sensors look the processes up.
    proc_root: PathBuf,
    sampler: IoSampler,
    count: usize,
    min_bytes_per_sec: f64,
    sample_period: Duration,
    /// The monotonic time of the last snapshot consumed (each is consumed once).
    last_mono_ns: Option<u64>,
    /// The wall clock of the last snapshot consumed, which the day's accounting measures from.
    last_wall_ms: Option<i64>,
    /// Bytes per name since the last speed post, and the helper's monotonic time of the regular
    /// snapshot it started at. A final snapshot in between adds to it.
    pending: BTreeMap<String, NameIo>,
    pending_since_ns: Option<u64>,
    /// The day of the last snapshot taken in a day's last seconds, until its totals are posted
    /// (a post that failed entirely is tried again on the next ticks).
    day_to_post: Option<(i64, i64)>,
    /// The last day a snapshot in its last seconds was consumed for: a day that ends unposted
    /// although it had one failed to post, rather than missed its window.
    window_day: Option<i64>,
    ledger: DayLedger,
    writers: Family<'c>,
    readers: Family<'c>,
    written_per_day: Family<'c>,
    read_per_day: Family<'c>,
    local: LocalTime,
    /// The probe's monotonic time at its first tick: a missing snapshot is a problem only once
    /// the helper has had three periods to write one.
    started_ns: Option<u64>,
    /// The monotonic time of the newest fresh snapshot seen: what an outage is measured from.
    last_good_ns: Option<u64>,
    /// An outage was reported (WARN) and has not ended (INFO) yet.
    in_outage: bool,
    baseline_logged: bool,
    /// The day whose name cap was logged: once per day, so a later day is not silent.
    day_cap_logged: Option<i64>,
    accounting: FailureLog,
}

impl<'c> TopDiskIo<'c> {
    fn new(collector: &'c Collector, config: &TopDiskIoConfig, snapshot: &Path) -> Self {
        let count = config.count();
        let max_tracked = max_tracked_names(count);
        Self {
            collector,
            snapshot_path: snapshot.to_path_buf(),
            proc_root: PathBuf::from(PROC_ROOT),
            sampler: IoSampler::new(),
            count,
            min_bytes_per_sec: config.min_bytes_per_sec,
            sample_period: config.period(),
            last_mono_ns: None,
            last_wall_ms: None,
            pending: BTreeMap::new(),
            pending_since_ns: None,
            day_to_post: None,
            window_day: None,
            ledger: DayLedger::default(),
            writers: Family::new(WRITERS, max_tracked),
            readers: Family::new(READERS, max_tracked),
            written_per_day: Family::new(WRITTEN_PER_DAY, max_tracked),
            read_per_day: Family::new(READ_PER_DAY, max_tracked),
            local: local_time,
            started_ns: None,
            last_good_ns: None,
            in_outage: false,
            baseline_logged: false,
            day_cap_logged: None,
            accounting: FailureLog::default(),
        }
    }

    /// One tick at `now_ns` (`CLOCK_MONOTONIC`, the helper's clock too): consume the snapshot if
    /// it is new and fresh, report an outage of the helper, retry a failed day post.
    fn tick(&mut self, now_ns: u64, logger: &Logger) {
        let started = *self.started_ns.get_or_insert(now_ns);
        let stale_after =
            u64::try_from((self.sample_period * STALE_PERIODS).as_nanos()).unwrap_or(u64::MAX);
        let snapshot = std::fs::read_to_string(&self.snapshot_path)
            .map_err(|error| format!("cannot read {}: {error}", self.snapshot_path.display()))
            .and_then(|text| Snapshot::parse(&text));
        let fresh = |snapshot: &Snapshot| snapshot.mono_ns.saturating_add(stale_after) >= now_ns;
        let reason = match &snapshot {
            Ok(snapshot) if fresh(snapshot) => {
                self.last_good_ns = Some(
                    self.last_good_ns
                        .map_or(snapshot.mono_ns, |good| good.max(snapshot.mono_ns)),
                );
                None
            }
            Ok(snapshot) => Some(format!(
                "{} is {} s old",
                self.snapshot_path.display(),
                now_ns.saturating_sub(snapshot.mono_ns) / 1_000_000_000
            )),
            Err(reason) => Some(reason.clone()),
        };
        // An outage is judged by the age of the last good snapshot (before the first one, of the
        // probe itself: the helper gets three periods), not by whether the file is there at this
        // very tick — a helper restarting is not an outage. One WARN per outage, one INFO at its
        // end.
        let outage = now_ns.saturating_sub(self.last_good_ns.unwrap_or(started)) >= stale_after;
        if outage && !self.in_outage {
            logger.warn(format!(
                "{WHAT}: no fresh snapshot from the helper ({}); nothing is posted until \
                 {HELPER_UNIT} writes one again. It runs where the top-level topDiskIo block of \
                 /etc/hsm-linux-probe/config.json is enabled and the bundle's install.sh was re-run \
                 (it enables the unit); check `systemctl status {HELPER_UNIT}` (README, Top disk \
                 I/O processes). Logged once per outage.",
                reason.as_deref().unwrap_or("none for three periods")
            ));
            self.in_outage = true;
        } else if !outage && self.in_outage {
            logger.info(format!("{WHAT}: the helper's snapshots are fresh again"));
            self.in_outage = false;
        }
        if let Ok(snapshot) = snapshot {
            if fresh(&snapshot) && self.last_mono_ns != Some(snapshot.mono_ns) {
                self.last_mono_ns = Some(snapshot.mono_ns);
                self.consume(&snapshot, logger);
            }
        }
        if let Some((day, wall_ms)) = self.day_to_post {
            // Retried only while the ledger still counts that day: once a snapshot after midnight
            // turned it (and the missed day was logged), the totals are another day's.
            if self.ledger.day != Some(day) || self.ledger.posted_day.is_some_and(|p| p >= day) {
                self.day_to_post = None;
            } else {
                self.post_day(day, wall_ms, logger);
            }
        }
    }

    fn consume(&mut self, snapshot: &Snapshot, logger: &Logger) {
        let read = self.sampler.consume(snapshot);
        self.report(&read, logger);
        for note in &read.notes {
            logger.log(Level::Debug, &format!("{WHAT}: {note}"));
        }
        let previous_wall = self.last_wall_ms.replace(snapshot.wall_ms);
        match previous_wall.filter(|_| !read.baseline) {
            None => self.pending_since_ns = Some(snapshot.mono_ns),
            Some(previous_wall) => {
                self.count_day(&read.deltas, previous_wall, snapshot.wall_ms, logger);
                for (name, io) in &read.deltas {
                    self.pending.entry(name.clone()).or_default().merge(io);
                }
                if snapshot.kind == Kind::Regular {
                    self.post_speeds(snapshot.mono_ns, logger);
                }
            }
        }
        let (day, second) = (self.local)(snapshot.wall_ms);
        if second + FINAL_WINDOW_SECONDS >= 86_400 {
            self.day_to_post = Some((day, snapshot.wall_ms));
            self.window_day = Some(day);
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
            let cause = if self.window_day == Some(missed) {
                format!(
                    "a snapshot was taken in the day's last {FINAL_WINDOW_SECONDS} s, but every \
                     post of its totals failed (the post errors are logged above)"
                )
            } else {
                format!(
                    "no snapshot was taken in the day's last {FINAL_WINDOW_SECONDS} s (a suspended \
                     host or a stopped helper)"
                )
            };
            logger.warn(format!(
                "{WHAT}: the per-day totals of {} are not posted: {cause}; they are never posted \
                 late",
                day_label(missed)
            ));
        }
    }

    /// The baseline once; a snapshot with no readable counters at all as an error episode.
    fn report(&mut self, read: &IoRead, logger: &Logger) {
        if read.baseline && !self.baseline_logged {
            logger.info(format!(
                "{WHAT}: baseline over {} processes, {} with readable I/O counters",
                read.processes, read.readable
            ));
            self.baseline_logged = true;
        }
        if read.processes > 0 && read.readable == 0 {
            self.accounting.failed(
                logger,
                WHAT,
                "the helper read no /proc/<pid>/io at all (a kernel without task I/O accounting, \
                 CONFIG_TASK_IO_ACCOUNTING? or the helper lost its capabilities: journalctl -u \
                 hsm-linux-probe-io)",
            );
        } else if read.readable > 0 {
            self.accounting.succeeded(logger, WHAT);
        }
    }

    /// Post the speeds over the interval since the previous regular snapshot.
    fn post_speeds(&mut self, mono_ns: u64, logger: &Logger) {
        let Some(since) = self.pending_since_ns.replace(mono_ns) else {
            return;
        };
        let seconds = mono_ns.saturating_sub(since) as f64 / 1e9;
        let pending = std::mem::take(&mut self.pending);
        let proc_root = self.proc_root.clone();
        let (count, period, min) = (self.count, self.sample_period, self.min_bytes_per_sec);
        for direction in [Direction::Write, Direction::Read] {
            let posts: Vec<Post> = sampler::select_top(&pending, direction, seconds, min, count)
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
    /// there was nothing to post; when every post failed, the next tick tries again.
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
            self.day_to_post = None;
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
        TICK
    }

    fn sample(&mut self, logger: &Logger) {
        self.tick(monotonic_ns(), logger);
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

#[cfg(test)]
mod tests {
    use super::sampler::tests::{entry, MB};
    use super::*;
    use crate::probe_only::disks::written::tests::{utc, MIDNIGHT};
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;
    use hsm_linux_probe_io::snapshot::{write_atomic, Entry};
    use std::sync::{Arc, Mutex};

    const SEC: u64 = 1_000_000_000;
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

    fn count(lines: &Arc<Mutex<Vec<String>>>, needle: &str) -> usize {
        lines
            .lock()
            .unwrap()
            .iter()
            .filter(|l| l.contains(needle))
            .count()
    }

    fn registrations(collector: &Collector, category: &str) -> Vec<String> {
        collector
            .registrations()
            .into_iter()
            .filter(|json| json.contains(&format!("\"Path\":\"{category}/")))
            .collect()
    }

    /// The source on a UTC "local" calendar, reading `<tree>/snapshot`.
    fn source<'c>(collector: &'c Collector, tree: &FakeTree) -> TopDiskIo<'c> {
        let mut source = TopDiskIo::new(collector, &enabled(), &tree.0.join("snapshot"));
        source.local = utc;
        source
    }

    /// What the helper would write: a snapshot taken at `mono_s` (monotonic) / `wall_ms`.
    fn helper_writes(tree: &FakeTree, mono_s: u64, wall_ms: i64, kind: Kind, entries: Vec<Entry>) {
        let snapshot = Snapshot {
            seq: mono_s,
            mono_ns: mono_s * SEC,
            wall_ms,
            kind,
            entries,
        };
        write_atomic(&tree.0.join("snapshot"), &snapshot.render()).expect("write");
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
        let snapshot = tree.0.join("snapshot");
        assert!(register(&collector, &TopDiskIoConfig::default(), &snapshot, &logger).is_none());
        assert_eq!(count(&lines, "off"), 1);
        let source = register(&collector, &enabled(), &snapshot, &logger).expect("on");
        assert_eq!(source.period(), TICK);
    }

    #[test]
    fn the_speeds_register_at_runtime_for_the_top_names_only() {
        let tree = FakeTree::new("topio-wire");
        let collector = test_collector();
        let (logger, _) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let noon = MIDNIGHT + 12 * 3600 * 1000;
        helper_writes(
            &tree,
            100,
            noon,
            Kind::Regular,
            vec![
                entry(100, "postgres", 1, 50, 0, 0),
                entry(101, "postgres", 1, 51, 0, 0),
                entry(200, "quiet", 1, 60, 0, 0),
            ],
        );
        source.tick(101 * SEC, &logger);
        assert!(
            registrations(&collector, WRITERS).is_empty(),
            "the baseline"
        );

        // 60 s later: postgres wrote 90 MB (1.5 MB/s), read 120 MB; "quiet" 59 MB is under 1 MB/s.
        helper_writes(
            &tree,
            160,
            noon + MIN_MS,
            Kind::Regular,
            vec![
                entry(100, "postgres", 1, 50, 120 * MB, 60 * MB),
                entry(101, "postgres", 1, 51, 0, 30 * MB),
                entry(200, "quiet", 1, 60, 0, 59 * MB),
            ],
        );
        source.tick(161 * SEC, &logger);
        // The same snapshot again: consumed once.
        source.tick(166 * SEC, &logger);
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

    /// The day: counted from every snapshot, posted once from the one in its last 30 s, then the
    /// next day starts — and a process that exits between two snapshots is credited to its reaper
    /// for its last partial interval, never counted twice.
    #[test]
    fn the_day_is_posted_once_in_its_last_seconds_and_rolls_over() {
        let tree = FakeTree::new("topio-day");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let day = utc(MIDNIGHT).0;
        // 23:57:00 baseline, 23:58:00 and 23:59:00 regular, 23:59:35 the final scan.
        let base_ms = MIDNIGHT + 24 * 3600 * 1000 - 3 * MIN_MS;
        let bash = |write| entry(10, "bash", 1, 5, 0, write);
        let dd = |write| entry(11, "dd", 10, 6, 0, write);
        let rsync = |read, write| entry(20, "rsync", 1, 7, read, write);
        let mut step = |mono: u64, wall: i64, kind: Kind, entries: Vec<Entry>| {
            helper_writes(&tree, mono, wall, kind, entries);
            source.tick((mono + 1) * SEC, &logger);
        };
        step(0, base_ms, Kind::Regular, vec![bash(0), dd(0), rsync(0, 0)]);
        step(
            60,
            base_ms + MIN_MS,
            Kind::Regular,
            vec![bash(0), dd(2_000 * MB), rsync(500 * MB, 100 * MB)],
        );
        step(
            120,
            base_ms + 2 * MIN_MS,
            Kind::Regular,
            vec![bash(0), dd(3_000 * MB), rsync(500 * MB, 100 * MB)],
        );
        // dd wrote 500 MB more, exited and was reaped by bash: bash's counter jumps by dd's whole
        // 3 500 MB; bash itself wrote 10 MB.
        step(
            155,
            base_ms + 2 * MIN_MS + 35_000,
            Kind::Final,
            vec![bash(10 * MB + 3_500 * MB), rsync(500 * MB, 100 * MB)],
        );
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
        // dd and rsync at 23:58, dd at 23:59; the final scan posts no speeds.
        assert_eq!(sent(&source.writers).len(), 3);

        // A later tick re-reads the same final snapshot: nothing more. What is written after the
        // post counts towards the next day.
        source.tick(160 * SEC, &logger);
        helper_writes(
            &tree,
            180,
            base_ms + 3 * MIN_MS,
            Kind::Regular,
            vec![bash(10 * MB + 3_500 * MB), rsync(500 * MB, 2_100 * MB)],
        );
        source.tick(181 * SEC, &logger);
        assert_eq!(source.written_per_day.sent.len(), 3);
        assert_eq!(source.ledger.day, Some(day + 1));
        assert_eq!(source.ledger.names["rsync"].written, 2_000 * MB);
        assert_eq!(count(&lines, "|WARN|") + count(&lines, "|ERROR|"), 0);
        collector.stop().expect("stop");
    }

    #[test]
    fn a_missed_window_is_logged_once_and_never_posted_late() {
        let tree = FakeTree::new("topio-missed");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        collector.start().expect("start");
        let evening = MIDNIGHT + 24 * 3600 * 1000 - 2 * MIN_MS;
        let rsync = |write| vec![entry(20, "rsync", 1, 7, 0, write)];
        helper_writes(&tree, 0, evening, Kind::Regular, rsync(0));
        source.tick(SEC, &logger);
        helper_writes(&tree, 60, evening + MIN_MS, Kind::Regular, rsync(100 * MB));
        source.tick(61 * SEC, &logger);
        // No scan in the window: the next one is after midnight.
        helper_writes(
            &tree,
            150,
            evening + 150_000,
            Kind::Regular,
            rsync(200 * MB),
        );
        source.tick(151 * SEC, &logger);
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
        assert!(warned[0].contains("no snapshot was taken"), "{}", warned[0]);
        collector.stop().expect("stop");
    }

    /// No snapshot (the helper is not running), then a stale one: one WARN per outage, after the
    /// helper had three periods; nothing posted; one INFO at its end; a second outage warns again.
    #[test]
    fn a_missing_or_stale_snapshot_is_one_warning_per_outage_and_posts_nothing() {
        let tree = FakeTree::new("topio-stale");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        let noon = MIDNIGHT + 12 * 3600 * 1000;
        source.tick(1_000 * SEC, &logger);
        source.tick(1_100 * SEC, &logger);
        assert_eq!(count(&lines, "|WARN|"), 0, "the helper gets three periods");
        source.tick(1_180 * SEC, &logger);
        source.tick(1_300 * SEC, &logger);
        assert_eq!(count(&lines, "|WARN|"), 1);
        assert!(count(&lines, HELPER_UNIT) >= 1);

        // A stale snapshot is not consumed.
        helper_writes(
            &tree,
            1_000,
            noon,
            Kind::Regular,
            vec![entry(1, "x", 0, 1, 0, 0)],
        );
        source.tick(1_400 * SEC, &logger);
        assert_eq!(source.last_mono_ns, None);

        // Fresh again: consumed, one INFO.
        helper_writes(
            &tree,
            1_400,
            noon,
            Kind::Regular,
            vec![entry(1, "x", 0, 1, 0, 0)],
        );
        source.tick(1_401 * SEC, &logger);
        assert_eq!(source.last_mono_ns, Some(1_400 * SEC));
        assert_eq!(count(&lines, "fresh again"), 1);

        // The helper restarts: the file is gone for a moment — not an outage.
        std::fs::remove_file(tree.0.join("snapshot")).expect("rm");
        source.tick(1_410 * SEC, &logger);
        helper_writes(
            &tree,
            1_415,
            noon,
            Kind::Regular,
            vec![entry(1, "x", 0, 1, 0, 0)],
        );
        source.tick(1_416 * SEC, &logger);
        assert_eq!(count(&lines, "|WARN|"), 1);

        // The helper stops for good (its last snapshot stays): a second outage, a second WARN
        // three periods after the last good snapshot, and a second INFO when it is back.
        source.tick(1_590 * SEC, &logger);
        assert_eq!(count(&lines, "|WARN|"), 1, "not yet three periods");
        source.tick(1_596 * SEC, &logger);
        source.tick(1_700 * SEC, &logger);
        assert_eq!(count(&lines, "|WARN|"), 2);
        assert!(count(&lines, "s old") >= 1, "the reason names the age");
        helper_writes(
            &tree,
            1_800,
            noon,
            Kind::Regular,
            vec![entry(1, "x", 0, 1, 0, 0)],
        );
        source.tick(1_801 * SEC, &logger);
        assert_eq!(count(&lines, "fresh again"), 2);
        assert!(registrations(&collector, WRITERS).is_empty());
    }

    /// A day post that failed entirely is retried only while the ledger still counts that day:
    /// after a snapshot past midnight it would post the next day's first minutes as this day.
    #[test]
    fn a_failed_day_post_is_not_retried_with_the_next_days_totals() {
        let tree = FakeTree::new("topio-retry");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        let day = utc(MIDNIGHT).0;
        let evening = MIDNIGHT + 24 * 3600 * 1000 - 2 * MIN_MS;
        let rsync = |write| vec![entry(20, "rsync", 1, 7, 0, write)];
        helper_writes(&tree, 0, evening, Kind::Regular, rsync(0));
        source.tick(SEC, &logger);
        helper_writes(&tree, 60, evening + MIN_MS, Kind::Regular, rsync(100 * MB));
        source.tick(61 * SEC, &logger);
        // Every post fails (the collector is gone): the final snapshot cannot post the day.
        collector.dispose();
        helper_writes(&tree, 95, evening + 95_000, Kind::Final, rsync(150 * MB));
        source.tick(96 * SEC, &logger);
        assert_eq!(
            source.day_to_post.map(|(d, _)| d),
            Some(day),
            "kept for a retry"
        );
        source.tick(101 * SEC, &logger);
        assert_eq!(
            source.day_to_post.map(|(d, _)| d),
            Some(day),
            "still that day: retried"
        );
        // Past midnight the ledger counts the next day: the retry is dropped, the day is
        // reported as missed, never posted with the next day's bytes.
        helper_writes(
            &tree,
            125,
            evening + 125_000,
            Kind::Regular,
            rsync(160 * MB),
        );
        source.tick(126 * SEC, &logger);
        assert_eq!(source.ledger.day, Some(day + 1));
        assert_eq!(source.day_to_post, None);
        assert_eq!(count(&lines, "are not posted"), 1);
        // A window snapshot was there: the warning names the failed posts, not a missing scan.
        assert_eq!(count(&lines, "every post of its totals failed"), 1);
        assert_eq!(count(&lines, "no snapshot was taken"), 0);
        assert!(source.written_per_day.sent.is_empty());
    }

    #[test]
    fn a_snapshot_without_any_counters_is_an_error_episode() {
        let tree = FakeTree::new("topio-noio");
        let collector = test_collector();
        let (logger, lines) = capture();
        let mut source = source(&collector, &tree);
        let mut blind = entry(1, "init", 0, 1, 0, 0);
        blind.counters = None;
        for mono in [0, 60] {
            helper_writes(&tree, mono, MIDNIGHT, Kind::Regular, vec![blind.clone()]);
            source.tick((mono + 1) * SEC, &logger);
        }
        assert_eq!(count(&lines, "|ERROR|"), 1);
        assert_eq!(count(&lines, "CONFIG_TASK_IO_ACCOUNTING"), 1);
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
        assert_eq!(count(&lines, "the cap"), 2, "one per day");
    }

    #[test]
    fn new_names_stop_at_the_cap() {
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
        assert_eq!(family.sensors.len(), 2);
        assert_eq!(family.sent.len(), 3, "a, b and a again");
        assert_eq!(count(&lines, "the cap"), 1);
    }
}
