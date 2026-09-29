//! Probe-only sensors: the sensors that exist ONLY in this Linux probe.
//!
//! By owner decision they are not part of the shared collector catalog (`src/native/collector`)
//! and never appear on Windows or in the managed collector. They go through the collector's
//! public sensor API, so wire format, queuing, batching, retry and TLS stay the library's; what
//! lives here is only the acquisition (a sysfs/statvfs read) and the schedule.
//!
//! The set is pinned separately from the managed-parity set, by
//! `probe::tests::the_registered_set_is_the_parity_set_plus_the_probe_only_set`, and documented in
//! `src/probe-linux/README.md` ("Probe-only sensors").
//!
//! # Structure
//!
//! Each source registers its own sensors (with their alerts, which must be attached before the
//! collector starts) and returns a [`Source`] that the probe drives on a thread of its own:
//!
//! * [`host`] — `.computer/Logical cores`, `.computer/CPU temperature`;
//! * [`disks`] — per mounted real filesystem, `.computer/Disks monitoring/Free space on <name>
//!   disk` (MB, %), `Free inodes on <name> disk %` and `Average disk write speed on <name> disk`
//!   (#1481); new mounts register at runtime;
//! * [`docker`] — `<module>/Docker/<project>/<service>/…`, per Compose service (#1416). It
//!   registers the services it finds before Start and any that appear later at runtime.
//!
//! # Failure isolation (root rules #6 and #8)
//!
//! * Every source runs on its own thread, so a source whose read blocks (a hung filesystem) cannot
//!   stall another's samples.
//! * Every sample runs under `catch_unwind`: a panicking source logs and keeps its schedule.
//! * A failed read is never posted as a value (no 0 for "unknown"): the sample is skipped and the
//!   failure is logged once, deduplicated by [`FailureLog`] until the source recovers.

pub mod disks;
pub mod docker;
pub mod host;

use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;
use std::sync::{Condvar, Mutex};
use std::time::{Duration, Instant};

use hsm_collector::Collector;

use crate::config::ProbeConfig;
use crate::logging::Logger;

/// Where the sources read the host from. Production uses [`HostEnvironment::system`]; tests point
/// it at a fake tree so the registered set does not depend on the machine running the tests.
#[derive(Clone, Debug)]
pub struct HostEnvironment {
    /// The sysfs root (`/sys`).
    pub sys_root: PathBuf,
    /// This process's mount table (`/proc/self/mountinfo`).
    pub mountinfo: PathBuf,
    /// The static filesystem table (`/etc/fstab`), to name real filesystems the unit hides.
    pub fstab: PathBuf,
    /// The kernel's per-device I/O counters (`/proc/diskstats`).
    pub diskstats: PathBuf,
    /// Filesystem statistics for a path (`statvfs(3)`); injectable for tests.
    pub statvfs: fn(&std::path::Path) -> std::io::Result<disks::FsStats>,
    /// Online logical CPUs (`sysconf(_SC_NPROCESSORS_ONLN)`); injectable for tests.
    pub online_cpus: fn() -> std::io::Result<i32>,
    /// The Docker Engine API client for a config (the socket); injectable for tests.
    pub docker_engine:
        fn(&crate::config::DockerConfig) -> Box<dyn docker::engine::EngineApi + Send>,
    /// The Docker source's state file (`$STATE_DIRECTORY/docker-state.json`); `None` keeps the
    /// state in memory only.
    pub docker_state: Option<PathBuf>,
    /// The persisted disk names (`$STATE_DIRECTORY/disk-names.json`); `None` keeps them in memory.
    pub disk_names: Option<PathBuf>,
    /// Today's written volume per disk (`$STATE_DIRECTORY/disk-written.json`); `None` keeps it in
    /// memory.
    pub disk_written: Option<PathBuf>,
    /// This boot's id (`/proc/sys/kernel/random/boot_id`): the diskstats counters restart at boot.
    pub boot_id: PathBuf,
}

impl HostEnvironment {
    pub fn system() -> Self {
        Self {
            sys_root: PathBuf::from("/sys"),
            mountinfo: PathBuf::from("/proc/self/mountinfo"),
            fstab: PathBuf::from("/etc/fstab"),
            diskstats: PathBuf::from("/proc/diskstats"),
            statvfs: disks::statvfs,
            online_cpus: host::online_cpus,
            docker_engine: |config| Box::new(docker::Engine::new(config.socket.clone())),
            docker_state: Some(docker::state::default_state_path(
                std::env::var_os("STATE_DIRECTORY").as_deref(),
            )),
            disk_names: Some(
                docker::state::default_state_path(std::env::var_os("STATE_DIRECTORY").as_deref())
                    .with_file_name(disks::names::FILE_NAME),
            ),
            disk_written: Some(
                docker::state::default_state_path(std::env::var_os("STATE_DIRECTORY").as_deref())
                    .with_file_name(disks::written::FILE_NAME),
            ),
            boot_id: PathBuf::from("/proc/sys/kernel/random/boot_id"),
        }
    }
}

/// A registered probe-only source: sampled once right after the collector starts, then every
/// [`Source::period`].
pub trait Source: Send {
    /// Short name for the log ("cpu temperature", "disk").
    fn name(&self) -> &'static str;
    fn period(&self) -> Duration;
    /// Take one sample and post it. Must not block for long; failures are the source's to log.
    fn sample(&mut self, logger: &Logger);
    /// The probe is stopping: save what must survive a restart. Must not block for long (it runs
    /// inside the probe's stop wait for its source threads).
    fn stop(&mut self, _logger: &Logger) {}
}

/// Register every enabled probe-only source, every Docker project in the main product (the test
/// harness). See [`register_routed`].
#[cfg(test)]
pub fn register<'c>(
    collector: &'c Collector,
    config: &ProbeConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    register_routed(
        collector,
        docker::Routes::main_only(collector),
        config,
        environment,
        logger,
    )
}

/// Register every enabled probe-only source. Call before `Collector::start`: the alerts are part of
/// the registration, and a value posted before Start would be dropped by the collector anyway.
/// `docker_routes` says which collector (HSM product) each Compose project reports into; the host
/// and disk sensors always go to `collector`, the main one.
///
/// A source that cannot register logs why and is left out; the others are unaffected.
pub fn register_routed<'c>(
    collector: &'c Collector,
    docker_routes: docker::Routes<'c>,
    config: &ProbeConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    let mut sources: Vec<Box<dyn Source + 'c>> = Vec::new();
    sources.extend(register_host_sources(
        collector,
        config,
        environment,
        logger,
    ));
    sources.extend(register_disk_sources(
        collector,
        config,
        environment,
        logger,
    ));
    sources.extend(docker::register(
        docker_routes,
        &config.docker,
        (environment.docker_engine)(&config.docker),
        environment.docker_state.clone(),
        environment.sys_root.clone(),
        logger,
    ));
    sources
}

fn register_host_sources<'c>(
    collector: &'c Collector,
    config: &ProbeConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    let mut sources: Vec<Box<dyn Source + 'c>> = Vec::new();
    let host = &config.host_sensors;
    if !host.enabled {
        logger.info("probe-only host sensors are disabled (probe.hostSensors.enabled = false)");
        return sources;
    }

    sources.extend(host::register_logical_cores(collector, environment, logger));
    if host.cpu_temperature {
        sources.extend(host::register_cpu_temperature(
            collector,
            environment,
            logger,
        ));
    } else {
        logger.info("CPU temperature sensor disabled (probe.hostSensors.cpuTemperature = false)");
    }
    sources
}

fn register_disk_sources<'c>(
    collector: &'c Collector,
    config: &ProbeConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    if config.host_sensors.disk.is_some() {
        logger.warn(
            "probe.hostSensors.disk is deprecated; use probe.disks.enabled (an explicit false is \
             still honoured while probe.disks.enabled is not set)",
        );
    }
    // Before 0.4.0 the host switches covered the disk sensor, so without an explicit
    // `probe.disks.enabled` they still do: an upgrade never switches the disks back on.
    if !config.disks_enabled() {
        logger.info(
            "disks: disabled (probe.disks.enabled = false, or hostSensors.enabled / hostSensors.disk \
             = false without a probe.disks.enabled)",
        );
        return Vec::new();
    }
    disks::register(collector, &config.disks, environment, logger)
}

/// Stop request shared by the source threads; waking a sleeping thread is immediate.
#[derive(Default)]
pub struct StopSignal {
    stopped: Mutex<bool>,
    wake: Condvar,
}

impl StopSignal {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn request(&self) {
        *self.stopped.lock().unwrap_or_else(|p| p.into_inner()) = true;
        self.wake.notify_all();
    }

    /// Sleep until `deadline` or a stop request. Returns `true` when stopping.
    fn wait_until(&self, deadline: Instant) -> bool {
        let mut stopped = self.stopped.lock().unwrap_or_else(|p| p.into_inner());
        loop {
            if *stopped {
                return true;
            }
            let now = Instant::now();
            if now >= deadline {
                return false;
            }
            stopped = self
                .wake
                .wait_timeout(stopped, deadline - now)
                .unwrap_or_else(|p| p.into_inner())
                .0;
        }
    }
}

/// Drive one source until `stop`: a sample now, then one every period on a fixed-rate schedule
/// (so a 5 s sampler puts exactly 60 samples into a 5-minute bar). A tick missed by more than a
/// whole period — the host was suspended, a read hung — is not replayed in a burst; the schedule
/// restarts from now.
pub fn run_source(source: &mut dyn Source, stop: &StopSignal, logger: &Logger) {
    let period = source.period();
    let mut next = Instant::now();
    loop {
        if catch_unwind(AssertUnwindSafe(|| source.sample(logger))).is_err() {
            logger.error(format!(
                "probe-only source '{}' panicked while sampling; it keeps its schedule",
                source.name()
            ));
        }
        next += period;
        let now = Instant::now();
        if next <= now {
            next = now + period;
        }
        if stop.wait_until(next) {
            if catch_unwind(AssertUnwindSafe(|| source.stop(logger))).is_err() {
                logger.error(format!(
                    "probe-only source '{}' panicked while stopping",
                    source.name()
                ));
            }
            return;
        }
    }
}

/// Deduplicated read-failure logging: the first failure (or a different one) is logged at ERROR,
/// repeats are suppressed, and the first success afterwards is logged at INFO. The probe's log is
/// the diagnostic channel for probe-only sources (root rule #8): a skipped sample is never silent.
#[derive(Debug, Default)]
pub struct FailureLog {
    current: Option<String>,
}

impl FailureLog {
    pub fn failed(&mut self, logger: &Logger, what: &str, reason: &str) {
        if self.current.as_deref() == Some(reason) {
            return;
        }
        logger.error(format!(
            "{what}: {reason} (sample skipped, nothing posted; repeats are not logged until it \
             recovers)"
        ));
        self.current = Some(reason.to_string());
    }

    pub fn succeeded(&mut self, logger: &Logger, what: &str) {
        if self.current.take().is_some() {
            logger.info(format!("{what}: recovered"));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::logging::Level;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;

    struct Counting {
        calls: Arc<AtomicUsize>,
        panic_on_first: bool,
    }

    impl Source for Counting {
        fn name(&self) -> &'static str {
            "counting"
        }
        fn period(&self) -> Duration {
            Duration::from_millis(10)
        }
        fn sample(&mut self, _logger: &Logger) {
            let call = self.calls.fetch_add(1, Ordering::SeqCst);
            if self.panic_on_first && call == 0 {
                panic!("a broken source");
            }
        }
    }

    #[test]
    fn a_panicking_sample_does_not_end_the_source() {
        let calls = Arc::new(AtomicUsize::new(0));
        let mut source = Counting {
            calls: Arc::clone(&calls),
            panic_on_first: true,
        };
        let stop = StopSignal::new();
        let logger = Logger::new(Level::Error, None);
        std::thread::scope(|scope| {
            scope.spawn(|| run_source(&mut source, &stop, &logger));
            while calls.load(Ordering::SeqCst) < 3 {
                std::thread::sleep(Duration::from_millis(5));
            }
            stop.request();
        });
        assert!(calls.load(Ordering::SeqCst) >= 3);
    }

    #[test]
    fn a_stop_request_wakes_a_sleeping_source_at_once() {
        struct Slow;
        impl Source for Slow {
            fn name(&self) -> &'static str {
                "slow"
            }
            fn period(&self) -> Duration {
                Duration::from_secs(3600)
            }
            fn sample(&mut self, _logger: &Logger) {}
        }
        let stop = StopSignal::new();
        let logger = Logger::new(Level::Error, None);
        let started = Instant::now();
        std::thread::scope(|scope| {
            scope.spawn(|| run_source(&mut Slow, &stop, &logger));
            std::thread::sleep(Duration::from_millis(20));
            stop.request();
        });
        assert!(started.elapsed() < Duration::from_secs(5));
    }

    #[test]
    fn failures_are_logged_once_until_recovery() {
        let lines = Arc::new(Mutex::new(Vec::new()));
        let logger = Logger::with_sink(Level::Debug, {
            let lines = Arc::clone(&lines);
            move |line: &str| lines.lock().unwrap().push(line.to_string())
        });
        let mut log = FailureLog::default();
        log.failed(&logger, "cpu temperature", "cannot read x: EIO");
        log.failed(&logger, "cpu temperature", "cannot read x: EIO");
        log.failed(&logger, "cpu temperature", "cannot read x: EIO");
        log.succeeded(&logger, "cpu temperature");
        log.succeeded(&logger, "cpu temperature");
        log.failed(&logger, "cpu temperature", "cannot read x: EIO");
        let lines = lines.lock().unwrap();
        let errors = lines.iter().filter(|l| l.contains("EIO")).count();
        let recoveries = lines.iter().filter(|l| l.contains("recovered")).count();
        assert_eq!((errors, recoveries), (2, 1), "{lines:#?}");
    }
}
