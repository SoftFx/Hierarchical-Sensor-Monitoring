//! Collector wiring and lifecycle.
//!
//! The lifecycle order mirrors `src/agent/src/agent_runtime.cpp`, the other native host of this
//! collector: build options -> install the log sink -> select the transport -> install the metric
//! sources -> register sensors -> start.
//!
//! The probe registers two separately pinned sets:
//!
//! * the **parity set** — exactly what the managed HSMDataCollector registers on Linux
//!   (`UnixSensorsCollection.AddAllDefaultSensors`); the contract is the parity table in
//!   `src/probe-linux/README.md`;
//! * the **probe-only set** — sensors that exist only in this probe, never in the shared catalog
//!   (`crate::probe_only`; README "Probe-only sensors").

use std::sync::Arc;
use std::time::{Duration, Instant};

use hsm_collector::{
    Collector, CollectorOptions, DefaultSensor, Error as CollectorError, LogLevel, SensorStatus,
    VersionSensor, LINUX_METRIC_SOURCES_AVAILABLE,
};

use crate::config::Config;
use crate::logging::{self, Logger};
use crate::probe_only::{self, HostEnvironment, StopSignal};
use crate::secret::{self, Secret};
use crate::shutdown;

/// Version reported as the probe's own `.module/Version` sensor.
pub const PROBE_VERSION: &str = env!("CARGO_PKG_VERSION");

/// How often the main thread wakes to re-check the stop flag. Bounds the SIGTERM response time.
const TICK: Duration = Duration::from_millis(200);

/// Build, start and run the probe until a stop is requested.
pub fn run(config: &Config, logger: Arc<Logger>) -> Result<(), Box<dyn std::error::Error>> {
    let collector = build_collector(config, Arc::clone(&logger))?;
    let product_version = register_sensors(&collector, &logger);
    // After the parity set and before Start: their alerts are part of the registration.
    let mut probe_only_sources = probe_only::register(
        &collector,
        &config.probe,
        &HostEnvironment::system(),
        &logger,
    );
    probe_only_sources.extend(probe_only::top_cpu::register(
        &collector,
        &config.top_cpu,
        std::path::Path::new(probe_only::top_cpu::PROC_ROOT),
        &logger,
    ));

    logger.info(format!(
        "starting: collector {} -> {}:{} (sensors under {})",
        hsm_collector::library_version_string(),
        config.hsm.address,
        config.hsm.port,
        tree_root(&config.hsm.computer_name, &config.hsm.module)
    ));

    collector.start()?;

    // After Start: the collector drops a value posted before it can accept data.
    if let Some(sensor) = &product_version {
        post_product_version(sensor, VersionEvent::Start, &logger);
    }

    // The shared catalog is sampled, queued and sent by the collector's own threads. Each
    // probe-only source gets a thread of its own (so one hung read cannot stall another), started
    // only now: a value posted before Start would be dropped. The main thread only waits.
    let stop_sources = StopSignal::new();
    let (exited_tx, exited_rx) = std::sync::mpsc::channel::<&'static str>();
    std::thread::scope(|scope| {
        let mut running = Vec::new();
        for source in probe_only_sources.iter_mut() {
            let (stop, logger, exited) = (&stop_sources, &logger, exited_tx.clone());
            let name = source.name();
            let spawned = std::thread::Builder::new()
                .name(format!("probe-{}", name.replace(' ', "-")))
                .spawn_scoped(scope, move || {
                    probe_only::run_source(source.as_mut(), stop, logger);
                    let _ = exited.send(name);
                });
            match spawned {
                Ok(_) => running.push(name),
                Err(error) => logger.error(format!(
                    "cannot start the '{name}' probe-only source thread: {error}"
                )),
            }
        }
        if !running.is_empty() {
            logger.info(format!("{} probe-only source(s) running", running.len()));
        }

        while !shutdown::is_requested() {
            std::thread::sleep(TICK);
        }

        // Stop the sources first, but only wait a bounded time for them: a source stuck inside a
        // read (a statvfs on a hung filesystem) cannot be interrupted, and it must not keep the
        // collector from draining what is already queued. The drain therefore runs INSIDE the
        // scope, and with a stuck thread the process exits after the drain instead of joining it.
        stop_sources.request();
        let all_stopped = await_sources(&exited_rx, running, &logger);
        stop_collector(&collector, product_version.as_ref(), config, &logger);
        if !all_stopped {
            // The drain is done; leaving the scope would join the stuck thread and hold the
            // process until systemd kills it. Exit instead: the collector is already stopped. A
            // non-zero code, so the unit's state shows the hang, not only the journal.
            logger.error("exiting without joining the stuck probe-only source thread(s)");
            std::process::exit(1);
        }
    });

    Ok(())
}

/// How long a stop waits for the probe-only source threads before draining without them.
const SOURCE_STOP_WAIT: Duration = Duration::from_secs(2);

/// Wait up to [`SOURCE_STOP_WAIT`] for every running source to report its exit; name any that did
/// not, so a stuck read is visible in the log instead of a silent stall. Returns whether all did.
fn await_sources(
    exited: &std::sync::mpsc::Receiver<&'static str>,
    mut running: Vec<&'static str>,
    logger: &Logger,
) -> bool {
    let deadline = Instant::now() + SOURCE_STOP_WAIT;
    while !running.is_empty() {
        let left = deadline.saturating_duration_since(Instant::now());
        match exited.recv_timeout(left) {
            Ok(name) => {
                if let Some(index) = running.iter().position(|running| *running == name) {
                    running.swap_remove(index);
                }
            }
            Err(_) => {
                logger.error(format!(
                    "probe-only source(s) {running:?} did not stop within {} ms (blocked in a \
                     read?); draining the collector without them",
                    SOURCE_STOP_WAIT.as_millis()
                ));
                return false;
            }
        }
    }
    true
}

/// Post the `Stop:` version marker, then stop the collector with its bounded drain.
fn stop_collector(
    collector: &Collector,
    product_version: Option<&VersionSensor<'_>>,
    config: &Config,
    logger: &Logger,
) {
    // Before Stop, so the value is still accepted and goes out with the stop drain — as the
    // managed ProductVersionSensor.StopAsync does.
    if let Some(sensor) = product_version {
        post_product_version(sensor, VersionEvent::Stop, logger);
    }

    logger.info("stop requested; draining the collector");
    let started = Instant::now();
    let stop_result = collector.stop();
    let elapsed = started.elapsed();
    match stop_result {
        Ok(()) => logger.info(format!("collector stopped in {} ms", elapsed.as_millis())),
        Err(error) => logger.error(format!("collector stop reported: {error}")),
    }
    // The hard bound on the drain is the collector's own capped stop; this only makes an overrun
    // visible in the log (and, through systemd's TimeoutStopSec, recoverable).
    if elapsed > config.shutdown_timeout() {
        logger.error(format!(
            "shutdown took {} ms, over the configured budget of {} ms",
            elapsed.as_millis(),
            config.shutdown_timeout().as_millis()
        ));
    }
}

fn build_collector(
    config: &Config,
    logger: Arc<Logger>,
) -> Result<Collector, Box<dyn std::error::Error>> {
    let key_path = secret::resolve_key_path(
        &config.hsm.access_key_file,
        std::env::var_os("CREDENTIALS_DIRECTORY").as_deref(),
    )?;
    let key = Secret::read_from_file(&key_path)?;
    if secret::is_readable_beyond_owner(&key_path) == Some(true) {
        logger.warn(format!(
            "the access-key file {} is readable beyond its owner; tighten it to 0400 \
             (or place it with LoadCredential=)",
            key_path.display()
        ));
    }

    let mut options = collector_options(config, key.expose());
    let collector = Collector::new(&options);
    // The collector copied the key into its own storage; wipe both of our copies now rather than
    // leaving them in the process image for the rest of the daemon's life.
    secret::wipe(&mut options.access_key);
    drop(key);
    let collector = collector?;

    let log_sink = Arc::clone(&logger);
    collector.set_logger(move |level: LogLevel, message: &str| {
        log_sink.log(logging::collector_message_level(level, message), message);
    })?;

    collector.use_http_transport()?;
    Ok(collector)
}

/// The collector options `config` asks for, with the access key handed in separately.
fn collector_options(config: &Config, access_key: &str) -> CollectorOptions {
    let mut options = CollectorOptions::new(access_key, config.hsm.address.trim(), config.hsm.port);
    // Only non-empty segments are handed over; the collector leaves an absent one out. By default
    // there is no computer name (#1493) and the module is `.probe` (#1496), so the tree is
    // `.computer/…` and `.probe/…` at the product root.
    if !config.hsm.module.is_empty() {
        options.module = Some(config.hsm.module.clone());
    }
    if !config.hsm.computer_name.is_empty() {
        options.computer_name = Some(config.hsm.computer_name.clone());
    }
    options.package_collect_period =
        Some(Duration::from_secs(config.hsm.package_collect_period_sec));
    options.request_timeout = Some(Duration::from_secs(config.hsm.request_timeout_sec));
    options
}

/// Where the tree sits, for the start log: the module node under the product root by default, and
/// a note when `computerName`/`module` move it (not recommended).
fn tree_root(computer_name: &str, module: &str) -> String {
    let nodes: Vec<&str> = [computer_name, module]
        .into_iter()
        .filter(|segment| !segment.is_empty())
        .collect();
    let place = if nodes.is_empty() {
        "the product root".to_string()
    } else {
        format!("'{}/'", nodes.join("/"))
    };
    if computer_name.is_empty() && module == crate::config::DEFAULT_MODULE {
        place
    } else {
        format!("{place} (computerName/module changed from the defaults; not recommended)")
    }
}

/// Register the managed Unix default set (`AddAllDefaultSensors` = computer set + module set) and
/// return the product-version handle for [`post_product_version`].
fn register_sensors<'c>(collector: &'c Collector, logger: &Logger) -> Option<VersionSensor<'c>> {
    register_computer_sensors(collector, logger);
    register_module_sensors(collector, logger)
}

/// The computer set (managed `AddAllComputerSensors`: system + disk), live only when this build can
/// feed it values.
fn register_computer_sensors(collector: &Collector, logger: &Logger) {
    match collector.install_linux_metric_sources() {
        Ok(()) => logger.info("Linux metric sources installed; the default host catalog is live"),
        Err(CollectorError::Unsupported { .. }) => logger.error(
            "the collector's Linux metric sources are unavailable (built without the \
             'linux-default-sensors' feature — see #1414): the default host catalog \
             (Total CPU, Free RAM, free disk and the three process sensors) will NOT be \
             reported and are therefore not registered",
        ),
        Err(error) => logger.error(format!("cannot install the Linux metric sources: {error}")),
    }

    if LINUX_METRIC_SOURCES_AVAILABLE {
        if let Err(error) = collector.add_all_computer_sensors() {
            logger.error(format!("cannot register the host catalog: {error}"));
        }
    } else {
        // Registering .computer sensors whose values can never arrive would show the operator a
        // tree of permanently expired nodes; skipping is the honest degradation.
        logger.info(
            "skipping the host catalog registration while the Linux metric sources are unavailable",
        );
    }
}

/// The module set (managed `AddAllModuleSensors`): process sensors, collector self-sensors, queue
/// diagnostics and the product version.
///
/// Registered one by one exactly as `src/agent` does, rather than through `add_all_module_sensors`:
/// that group also registers `Process ThreadPool thread count`, a CLR concept a native process can
/// only report as 0. The process node is left at the collector's fixed `Process process` name (no
/// process name passed) — deliberately the same path on every native host, so one HSM alert
/// template on `.module/Process process/…` applies to all of them (#1429). Do not name it per
/// process. Because the group helper is not called, the probe posts `.module/Version` itself.
fn register_module_sensors<'c>(
    collector: &'c Collector,
    logger: &Logger,
) -> Option<VersionSensor<'c>> {
    // Same gate as the computer catalog, for the same reason: the process sensors are
    // metric-source-fed (add_default_sensor marks every value-typed default sensor as a metric
    // candidate, and a candidate produces values only once a factory binds a reader at Start).
    // Without the Linux metric sources they would be permanently empty nodes in the tree.
    if LINUX_METRIC_SOURCES_AVAILABLE {
        for sensor in [
            DefaultSensor::ProcessCpu,
            DefaultSensor::ProcessMemory,
            DefaultSensor::ProcessThreadCount,
        ] {
            if let Err(error) = collector.add_default_sensor(sensor, None) {
                logger.error(format!("cannot register {sensor:?}: {error}"));
            }
        }
    }
    if let Err(error) = collector.add_collector_monitoring_sensors() {
        logger.error(format!(
            "cannot register the collector self-sensors: {error}"
        ));
    }
    if let Err(error) = collector.add_all_queue_diagnostic_sensors() {
        logger.error(format!("cannot register the queue diagnostics: {error}"));
    }
    match collector.product_version_sensor() {
        Ok(sensor) => Some(sensor),
        Err(error) => {
            logger.error(format!(
                "cannot register the product version sensor: {error}"
            ));
            None
        }
    }
}

/// When `.module/Version` is posted: the managed `ProductVersionSensor` posts on both.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum VersionEvent {
    Start,
    Stop,
}

/// The `.module/Version` comment exactly as the managed `ProductVersionSensor` writes it:
/// `Start: dd/MM/yyyy HH:mm:ss` / `Stop: …` (UTC, `SensorBase.DefaultTimeFormat`).
fn version_comment(event: VersionEvent, timestamp: &str) -> String {
    match event {
        VersionEvent::Start => format!("Start: {timestamp}"),
        VersionEvent::Stop => format!("Stop: {timestamp}"),
    }
}

/// Post the probe's version with a start or stop comment, mirroring the managed
/// `ProductVersionSensor.StartAsync`/`StopAsync`.
fn post_product_version(sensor: &VersionSensor<'_>, event: VersionEvent, logger: &Logger) {
    let (major, minor, build, revision) = parse_version(PROBE_VERSION);
    let comment = version_comment(event, &logging::managed_timestamp_now());
    if let Err(error) = sensor.add_with(
        major,
        minor,
        build,
        revision,
        SensorStatus::Ok,
        Some(&comment),
    ) {
        logger.error(format!("cannot post the product version: {error}"));
    }
}

/// `major.minor[.build[.revision]]`, mirroring the collector's `ParseVersionString`: leading
/// numeric components only, missing major/minor read as 0, missing build/revision as absent.
fn parse_version(text: &str) -> (i32, i32, Option<i32>, Option<i32>) {
    let mut parts = text
        .split('.')
        .map_while(|part| part.parse::<i32>().ok())
        .take(4);
    (
        parts.next().unwrap_or(0),
        parts.next().unwrap_or(0),
        parts.next(),
        parts.next(),
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::ProbeConfig;
    use crate::logging::Level;

    fn test_collector(port: u16) -> Collector {
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", port);
        options.allow_plaintext_transport = true;
        options.module = Some(crate::config::DEFAULT_MODULE.into());
        Collector::new(&options).expect("create")
    }

    /// The paths the probe registers, read back from the payloads the collector records at Start
    /// — the `/commands` registration batch. No transport is installed, so the in-memory sender
    /// receives it and nothing leaves the test.
    fn registered_paths() -> Vec<String> {
        registered_paths_with(&ProbeConfig::default(), false)
    }

    fn path_of(json: &str) -> Option<String> {
        let start = json.find("\"Path\":\"")? + "\"Path\":\"".len();
        let end = json[start..].find('"')? + start;
        Some(json[start..end].to_string())
    }

    /// The registration JSON of every sensor the probe registers — the parity set, plus the
    /// probe-only set against a fake host (a coretemp package sensor, a mounted target, and a
    /// Docker daemon serving the garage-server captures) when `with_probe_only` is set, so the
    /// result does not depend on the machine running the test.
    fn registrations_with(config: &ProbeConfig, with_probe_only: bool) -> Vec<String> {
        let collector = test_collector(1);
        let logger = Logger::new(Level::Error, None);
        let version = register_sensors(&collector, &logger);
        assert!(
            version.is_some(),
            "the product version sensor must register"
        );
        let host = FakeHost::new();
        let sources = if with_probe_only {
            probe_only::register(&collector, config, &host.environment(), &logger)
        } else {
            Vec::new()
        };
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        drop(sources);
        registrations
    }

    fn registered_paths_with(config: &ProbeConfig, with_probe_only: bool) -> Vec<String> {
        let mut paths: Vec<String> = registrations_with(config, with_probe_only)
            .iter()
            .filter_map(|json| path_of(json))
            .collect();
        paths.sort();
        paths
    }

    /// A fake host for the probe-only sources: sysfs with a coretemp package sensor, a mountinfo
    /// whose root mount holds the disk target, and fixed statvfs/sysconf answers.
    struct FakeHost {
        tree: crate::probe_only::host::tests::FakeTree,
    }

    impl FakeHost {
        fn new() -> Self {
            let tree = crate::probe_only::host::tests::FakeTree::new("host");
            tree.file("sys/class/hwmon/hwmon0/name", "coretemp\n");
            tree.file("sys/class/hwmon/hwmon0/temp1_label", "Package id 0\n");
            tree.file("sys/class/hwmon/hwmon0/temp1_input", "43000\n");
            // garage-server's mounts and block devices (captured 2026-09-29).
            tree.file(
                "mountinfo",
                crate::probe_only::disks::mounts::tests::GARAGE_MOUNTINFO,
            );
            tree.file(
                "diskstats",
                crate::probe_only::disks::diskstats::tests::GARAGE_DISKSTATS,
            );
            crate::probe_only::disks::diskstats::tests::garage_sysfs(&tree);
            Self { tree }
        }

        fn environment(&self) -> HostEnvironment {
            HostEnvironment {
                sys_root: self.tree.0.join("sys"),
                mountinfo: self.tree.0.join("mountinfo"),
                fstab: self.tree.0.join("fstab"),
                diskstats: self.tree.0.join("diskstats"),
                statvfs: |_| {
                    Ok(crate::probe_only::disks::FsStats {
                        fragment_size: 4096,
                        blocks: 1000,
                        blocks_available: 400,
                        files: 100,
                        files_available: 90,
                    })
                },
                online_cpus: || Ok(4),
                docker_engine: |_| {
                    Box::new(crate::probe_only::docker::tests::FixtureEngine::garage())
                },
                docker_state: None,
                disk_names: None,
                disk_written: None,
                boot_id: std::path::PathBuf::from("/nonexistent/boot_id"),
            }
        }
    }

    /// Sensors that exist only in this probe (README "Probe-only sensors"; owner decisions of
    /// 2026-09-24): computer-level, so they sit under `.computer/` at the product root (or under
    /// `computerName` when it is set), not the module.
    const PROBE_ONLY_SET: &[&str] = &[".computer/CPU temperature", ".computer/Logical cores"];

    /// The disk sensors garage-server registers (#1481), pinned literally: four real filesystems
    /// (`/` on sdc1, the FUSE-NTFS archives on sda2 and sdb1, ext4 on sdb5), deduplicated from
    /// eleven mounts, named after the Windows per-drive pattern; five sensors each (20 paths).
    const DISKS_GARAGE_SET: &[&str] = &[
        ".computer/Disks monitoring/Average disk write speed on mediacentr disk",
        ".computer/Disks monitoring/Average disk write speed on oldlinux disk",
        ".computer/Disks monitoring/Average disk write speed on root disk",
        ".computer/Disks monitoring/Average disk write speed on wd4tb disk",
        ".computer/Disks monitoring/Free inodes on mediacentr disk %",
        ".computer/Disks monitoring/Free inodes on oldlinux disk %",
        ".computer/Disks monitoring/Free inodes on root disk %",
        ".computer/Disks monitoring/Free inodes on wd4tb disk %",
        ".computer/Disks monitoring/Free space on mediacentr disk",
        ".computer/Disks monitoring/Free space on mediacentr disk %",
        ".computer/Disks monitoring/Free space on oldlinux disk",
        ".computer/Disks monitoring/Free space on oldlinux disk %",
        ".computer/Disks monitoring/Free space on root disk",
        ".computer/Disks monitoring/Free space on root disk %",
        ".computer/Disks monitoring/Free space on wd4tb disk",
        ".computer/Disks monitoring/Free space on wd4tb disk %",
        ".computer/Disks monitoring/Written per day on mediacentr disk",
        ".computer/Disks monitoring/Written per day on oldlinux disk",
        ".computer/Disks monitoring/Written per day on root disk",
        ".computer/Disks monitoring/Written per day on wd4tb disk",
    ];

    /// The module set: managed `AddAllModuleSensors` minus `Process ThreadPool thread count`
    /// (a CLR concept) and minus the metric-fed process sensors, which register only when
    /// the build can feed them (`METRIC_FED_SET`).
    const MODULE_SET: &[&str] = &[
        ".probe/.module/Collector errors",
        ".probe/.module/Collector queue stats/Items count in package",
        ".probe/.module/Collector queue stats/Package content size",
        ".probe/.module/Collector queue stats/Package process time",
        ".probe/.module/Collector queue stats/Queue overflow",
        ".probe/.module/Collector version",
        ".probe/.module/Service alive",
        ".probe/.module/Version",
    ];

    /// Everything the metric-source factory feeds: the computer set (managed
    /// `AddAllComputerSensors` on Unix) plus the process sensors. Registered only when the build
    /// has the Linux metric sources (#1414); without them these would be empty nodes.
    #[cfg(feature = "linux-default-sensors")]
    const METRIC_FED_SET: &[&str] = &[
        ".computer/Disks monitoring/Free space on disk",
        ".computer/Disks monitoring/Free space on disk prediction",
        ".computer/Free RAM memory",
        ".computer/Total CPU",
        ".probe/.module/Process process/Process CPU",
        ".probe/.module/Process process/Process memory",
        ".probe/.module/Process process/Process thread count",
    ];

    /// The Docker source (#1416) on garage-server: 12 Compose containers, 11 monitored services, all
    /// registered before Start. Every service gets the three state sensors, the four with a
    /// healthcheck (seaweedfs, mongo, gitea, db) `Health`, and all eleven (all running) `CPU`,
    /// `Memory used %` (the memory limit is in that description, not a sensor of its own) and —
    /// their stats carry a block-device write counter — `Disk written per hour`.
    /// `lingua-ci/ci-image` — Exited (0) under restart policy `no` — is a completed one-shot job
    /// and not monitored (owner decision). 70 paths, no empty nodes.
    const DOCKER_GARAGE_SET: &[&str] = &[
        ".probe/Docker/caddy/caddy/CPU",
        ".probe/Docker/caddy/caddy/Disk written per hour",
        ".probe/Docker/caddy/caddy/Memory used %",
        ".probe/Docker/caddy/caddy/OOM killed",
        ".probe/Docker/caddy/caddy/Restart count",
        ".probe/Docker/caddy/caddy/Service status",
        ".probe/Docker/gitea/db/CPU",
        ".probe/Docker/gitea/db/Disk written per hour",
        ".probe/Docker/gitea/db/Health",
        ".probe/Docker/gitea/db/Memory used %",
        ".probe/Docker/gitea/db/OOM killed",
        ".probe/Docker/gitea/db/Restart count",
        ".probe/Docker/gitea/db/Service status",
        ".probe/Docker/gitea/gitea/CPU",
        ".probe/Docker/gitea/gitea/Disk written per hour",
        ".probe/Docker/gitea/gitea/Health",
        ".probe/Docker/gitea/gitea/Memory used %",
        ".probe/Docker/gitea/gitea/OOM killed",
        ".probe/Docker/gitea/gitea/Restart count",
        ".probe/Docker/gitea/gitea/Service status",
        ".probe/Docker/hsm/app/CPU",
        ".probe/Docker/hsm/app/Disk written per hour",
        ".probe/Docker/hsm/app/Memory used %",
        ".probe/Docker/hsm/app/OOM killed",
        ".probe/Docker/hsm/app/Restart count",
        ".probe/Docker/hsm/app/Service status",
        ".probe/Docker/lingua-ci/dind/CPU",
        ".probe/Docker/lingua-ci/dind/Disk written per hour",
        ".probe/Docker/lingua-ci/dind/Memory used %",
        ".probe/Docker/lingua-ci/dind/OOM killed",
        ".probe/Docker/lingua-ci/dind/Restart count",
        ".probe/Docker/lingua-ci/dind/Service status",
        ".probe/Docker/lingua-ci/janitor/CPU",
        ".probe/Docker/lingua-ci/janitor/Disk written per hour",
        ".probe/Docker/lingua-ci/janitor/Memory used %",
        ".probe/Docker/lingua-ci/janitor/OOM killed",
        ".probe/Docker/lingua-ci/janitor/Restart count",
        ".probe/Docker/lingua-ci/janitor/Service status",
        ".probe/Docker/lingua-ci/runner-heavy/CPU",
        ".probe/Docker/lingua-ci/runner-heavy/Disk written per hour",
        ".probe/Docker/lingua-ci/runner-heavy/Memory used %",
        ".probe/Docker/lingua-ci/runner-heavy/OOM killed",
        ".probe/Docker/lingua-ci/runner-heavy/Restart count",
        ".probe/Docker/lingua-ci/runner-heavy/Service status",
        ".probe/Docker/lingua-ci/runner-light/CPU",
        ".probe/Docker/lingua-ci/runner-light/Disk written per hour",
        ".probe/Docker/lingua-ci/runner-light/Memory used %",
        ".probe/Docker/lingua-ci/runner-light/OOM killed",
        ".probe/Docker/lingua-ci/runner-light/Restart count",
        ".probe/Docker/lingua-ci/runner-light/Service status",
        ".probe/Docker/lingua/mongo/CPU",
        ".probe/Docker/lingua/mongo/Disk written per hour",
        ".probe/Docker/lingua/mongo/Health",
        ".probe/Docker/lingua/mongo/Memory used %",
        ".probe/Docker/lingua/mongo/OOM killed",
        ".probe/Docker/lingua/mongo/Restart count",
        ".probe/Docker/lingua/mongo/Service status",
        ".probe/Docker/lingua/seaweedfs/CPU",
        ".probe/Docker/lingua/seaweedfs/Disk written per hour",
        ".probe/Docker/lingua/seaweedfs/Health",
        ".probe/Docker/lingua/seaweedfs/Memory used %",
        ".probe/Docker/lingua/seaweedfs/OOM killed",
        ".probe/Docker/lingua/seaweedfs/Restart count",
        ".probe/Docker/lingua/seaweedfs/Service status",
        ".probe/Docker/portainer/portainer/CPU",
        ".probe/Docker/portainer/portainer/Disk written per hour",
        ".probe/Docker/portainer/portainer/Memory used %",
        ".probe/Docker/portainer/portainer/OOM killed",
        ".probe/Docker/portainer/portainer/Restart count",
        ".probe/Docker/portainer/portainer/Service status",
    ];

    #[test]
    fn the_registered_set_is_exactly_the_managed_unix_default_set() {
        // The parity contract (README table): nothing more, nothing less. A probe-only sensor, or a
        // managed sensor the probe stops registering, fails here.
        #[allow(unused_mut)]
        let mut expected: Vec<&str> = MODULE_SET.to_vec();
        #[cfg(feature = "linux-default-sensors")]
        expected.extend_from_slice(METRIC_FED_SET);
        expected.sort_unstable();

        assert_eq!(registered_paths(), expected);
    }

    fn parity_set() -> Vec<&'static str> {
        #[allow(unused_mut)]
        let mut expected: Vec<&str> = MODULE_SET.to_vec();
        #[cfg(feature = "linux-default-sensors")]
        expected.extend_from_slice(METRIC_FED_SET);
        expected
    }

    #[test]
    fn the_registered_set_is_the_parity_set_plus_the_probe_only_set() {
        // The second pinned contract: nothing more, nothing less. A probe-only sensor added
        // without updating PROBE_ONLY_SET (and the README table), or one that stops registering,
        // fails here — and so does a probe-only path that collides with the parity set.
        let mut expected = parity_set();
        expected.extend_from_slice(PROBE_ONLY_SET);
        expected.extend_from_slice(DISKS_GARAGE_SET);
        expected.extend_from_slice(DOCKER_GARAGE_SET);
        expected.sort_unstable();
        let registered = registered_paths_with(&ProbeConfig::default(), true);
        assert_eq!(registered, expected);

        let parity = parity_set();
        assert!(
            PROBE_ONLY_SET
                .iter()
                .chain(DISKS_GARAGE_SET)
                .chain(DOCKER_GARAGE_SET)
                .all(|path| !parity.contains(path)),
            "a probe-only sensor must never shadow a parity sensor"
        );
        // The literal list and the module's own path builders agree.
        let mut built = crate::probe_only::disks::tests::garage_paths();
        built.sort_unstable();
        assert_eq!(built, DISKS_GARAGE_SET);
    }

    #[test]
    fn host_sensors_switch_off_as_configured() {
        let mut config = ProbeConfig::default();
        config.host_sensors.enabled = false;
        config.disks.enabled = Some(false);
        config.docker.enabled = false;
        let mut parity = parity_set();
        parity.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), parity);

        let mut config = ProbeConfig::default();
        config.host_sensors.cpu_temperature = false;
        config.disks.enabled = Some(false);
        config.docker.enabled = false;
        let mut expected = parity_set();
        expected.push(".computer/Logical cores");
        expected.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), expected);

        // The deprecated 0.2.x switch still turns the disks off.
        let mut config = ProbeConfig::default();
        config.host_sensors.disk = Some(false);
        config.docker.enabled = false;
        let mut expected = parity_set();
        expected.extend_from_slice(PROBE_ONLY_SET);
        expected.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), expected);

        // Excluded mounts and the write-speed switch; an explicit probe.disks.enabled wins over
        // the host switch that used to cover the disks.
        let mut config = ProbeConfig::default();
        config.host_sensors.enabled = false;
        config.disks.enabled = Some(true);
        config.docker.enabled = false;
        config.disks.exclude = vec!["/mnt/*".into()];
        config.disks.write_speed = false;
        let mut expected = parity_set();
        expected.extend_from_slice(&[
            ".computer/Disks monitoring/Free inodes on root disk %",
            ".computer/Disks monitoring/Free space on root disk",
            ".computer/Disks monitoring/Free space on root disk %",
        ]);
        expected.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), expected);

        // The Docker source has its own switch, independent of the host sensors. Without a
        // probe.disks.enabled, hostSensors.enabled = false still covers the disks (as before
        // 0.4.0), so an upgraded config does not switch them back on.
        let mut config = ProbeConfig::default();
        config.host_sensors.enabled = false;
        let mut expected = parity_set();
        expected.extend_from_slice(DOCKER_GARAGE_SET);
        expected.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), expected);
    }

    #[test]
    fn probe_only_sensors_register_their_agreed_shape_and_alerts() {
        let registrations = registrations_with(&ProbeConfig::default(), true);
        let find = |path: &str| {
            registrations
                .iter()
                .find(|json| path_of(json).as_deref() == Some(path))
                .unwrap_or_else(|| panic!("{path} not registered"))
                .clone()
        };
        let contains_all = |json: &str, parts: &[&str]| {
            for part in parts {
                assert!(json.contains(part), "missing {part} in {json}");
            }
        };

        // Int, 48 h TTL, no alert.
        contains_all(
            &find(".computer/Logical cores"),
            &[
                "\"SensorType\":1,",
                "\"TTLTicks\":[1728000000000]",
                "\"Alerts\":null",
                "\"IsSingletonSensor\":true",
            ],
        );
        // DoubleBar, 15 min TTL, no unit (°C has no code), Mean > 80 warning band + > 90 error.
        contains_all(
            &find(".computer/CPU temperature"),
            &[
                "\"SensorType\":5,",
                "\"TTLTicks\":[9000000000]",
                "\"OriginalUnit\":null",
                "{\"Combination\":0,\"Operation\":2,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"80\"}},\
                 {\"Combination\":0,\"Operation\":0,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"90\"}}],\
                 \"Status\":1,",
                "\"Conditions\":[{\"Combination\":0,\"Operation\":2,\"Property\":103,\
                 \"Target\":{\"Type\":0,\"Value\":\"90\"}}],\"Status\":3,",
                "\"ScheduledRepeatMode\":20,\"ScheduledInstantSend\":true",
            ],
        );
        // Every filesystem, e.g. the archive: Double, MB, EMA, 15 min TTL, and no absolute-size
        // alert (a fixed 20 GB threshold would hold a small /boot/efi in Error; the % sensor
        // carries the alerts).
        contains_all(
            &find(".computer/Disks monitoring/Free space on wd4tb disk"),
            &[
                "\"SensorType\":2,",
                "\"TTLTicks\":[9000000000]",
                "\"OriginalUnit\":3,",
                "\"Statistics\":1",
                "\"IsSingletonSensor\":true",
                "\"Alerts\":null",
            ],
        );
        // DoubleBar, MBytes_sec, EMA, 15 min TTL, no alert (the Windows row has none); the
        // description names the whole disk and the filesystem sharing it.
        let write = find(".computer/Disks monitoring/Average disk write speed on mediacentr disk");
        contains_all(
            &write,
            &[
                "\"SensorType\":5,",
                "\"TTLTicks\":[9000000000]",
                "\"OriginalUnit\":2103,",
                "\"Statistics\":1",
                "\"Alerts\":null",
                "whole disk sdb",
                "also the write speed of: oldlinux",
            ],
        );
        // Double, Percents, 15 min TTL, < 10 warning band + < 5 error.
        contains_all(
            &find(".computer/Disks monitoring/Free space on root disk %"),
            &[
                "\"SensorType\":2,",
                "\"TTLTicks\":[9000000000]",
                "\"OriginalUnit\":100,",
                "{\"Combination\":0,\"Operation\":1,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"10\"}},\
                 {\"Combination\":0,\"Operation\":3,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"5\"}}],\
                 \"Status\":1,",
                "\"Conditions\":[{\"Combination\":0,\"Operation\":1,\"Property\":20,\
                 \"Target\":{\"Type\":0,\"Value\":\"5\"}}],\"Status\":3,",
            ],
        );
        // Double, Percents, 15 min TTL, < 10 warning only.
        let inodes = find(".computer/Disks monitoring/Free inodes on root disk %");
        contains_all(
            &inodes,
            &[
                "\"TTLTicks\":[9000000000]",
                "\"OriginalUnit\":100,",
                "\"Conditions\":[{\"Combination\":0,\"Operation\":1,\"Property\":20,\
                 \"Target\":{\"Type\":0,\"Value\":\"10\"}}],\"Status\":1,",
            ],
        );
        assert!(!inodes.contains("\"Status\":3"), "{inodes}");
    }

    #[test]
    #[cfg(feature = "linux-default-sensors")]
    fn the_process_node_keeps_the_shared_fixed_name_alert_templates_target() {
        // By design (#1429 closed as such): every native host — HsmAgent and this probe — registers
        // the same fixed ".probe/.module/Process process" node, so one HSM alert template on that path
        // applies to all of them. A per-process name would silently detach hosts from it.
        let paths = registered_paths();
        for sensor in ["Process CPU", "Process memory", "Process thread count"] {
            let expected = format!(".probe/.module/Process process/{sensor}");
            assert!(
                paths.iter().any(|path| path == &expected),
                "missing {expected} in {paths:#?}"
            );
        }
        assert!(
            paths
                .iter()
                .filter(|path| path.starts_with(".probe/.module/Process "))
                .all(|path| path.starts_with(".probe/.module/Process process/")),
            "no other process node may be registered: {paths:#?}"
        );
        assert!(
            paths.iter().all(|path| !path.contains("ThreadPool")),
            "a native process has no CLR thread pool: {paths:#?}"
        );
    }

    #[test]
    fn a_source_that_does_not_stop_is_named_and_does_not_block_the_drain() {
        let lines = Arc::new(std::sync::Mutex::new(Vec::new()));
        let logger = Logger::with_sink(Level::Debug, {
            let lines = Arc::clone(&lines);
            move |line: &str| lines.lock().unwrap().push(line.to_string())
        });
        let (tx, rx) = std::sync::mpsc::channel();
        tx.send("disk").unwrap();
        let started = Instant::now();
        // "cpu temperature" never reports: the wait gives up at its deadline instead of hanging.
        assert!(!await_sources(
            &rx,
            vec!["cpu temperature", "disk"],
            &logger
        ));
        assert!(started.elapsed() < SOURCE_STOP_WAIT + Duration::from_secs(2));
        let lines = lines.lock().unwrap();
        assert!(
            lines
                .iter()
                .any(|line| line.contains("\"cpu temperature\"") && !line.contains("\"disk\"")),
            "{lines:#?}"
        );

        // All sources reporting in returns without an error line.
        let (tx, rx) = std::sync::mpsc::channel();
        tx.send("disk").unwrap();
        let quiet = Logger::with_sink(Level::Debug, |line: &str| {
            panic!("nothing should be logged: {line}")
        });
        assert!(await_sources(&rx, vec!["disk"], &quiet));
    }

    /// A config with the given extra `hsm` keys (`, "module": …`).
    fn config_with(hsm_keys: &str) -> Config {
        Config::parse(&format!(
            r#"{{ "hsm": {{ "address": " https://garage.lan ", "port": 44330,
                "accessKeyFile": "access-key"{hsm_keys} }} }}"#
        ))
        .expect("parse")
    }

    /// Where the probe's `Service alive` lands with these options.
    fn service_alive_path(options: &CollectorOptions) -> Option<String> {
        let collector = Collector::new(options).expect("create");
        register_sensors(&collector, &Logger::new(Level::Error, None));
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        registrations
            .iter()
            .filter_map(|json| path_of(json))
            .find(|path| path.ends_with(".module/Service alive"))
    }

    #[test]
    fn the_config_reaches_the_collector_options() {
        // A minimal config (what the install bundle wrote before 0.7.0): no computer node, the
        // module node `.probe`.
        let options = collector_options(&config_with(""), "unit-test-key");
        assert_eq!(options.module.as_deref(), Some(".probe"));
        assert_eq!(options.computer_name, None);
        assert_eq!(options.server_address, "https://garage.lan");
        assert_eq!(options.port, 44330);
        assert_eq!(
            options.package_collect_period,
            Some(Duration::from_secs(15))
        );
        assert_eq!(options.request_timeout, Some(Duration::from_secs(30)));
        assert_eq!(
            service_alive_path(&options).as_deref(),
            Some(".probe/.module/Service alive")
        );
        // What the bundle writes since 0.7.0: the same layout, spelled out.
        let options = collector_options(
            &config_with(r#", "computerName": "", "module": ".probe""#),
            "unit-test-key",
        );
        assert_eq!(options.module.as_deref(), Some(".probe"));
        assert_eq!(options.computer_name, None, "never an empty segment");
        // Both keys emptied by hand: left out, never handed over as empty segments.
        let options = collector_options(
            &config_with(r#", "computerName": "", "module": """#),
            "unit-test-key",
        );
        assert_eq!((options.module, options.computer_name), (None, None));
        // A hand-written config that keeps both nodes: both survive.
        let options = collector_options(
            &config_with(r#", "computerName": "h", "module": "LinuxProbe""#),
            "unit-test-key",
        );
        assert_eq!(options.module.as_deref(), Some("LinuxProbe"));
        assert_eq!(options.computer_name.as_deref(), Some("h"));
        assert_eq!(
            service_alive_path(&options).as_deref(),
            Some("h/LinuxProbe/.module/Service alive")
        );
    }

    #[test]
    fn the_start_log_says_where_the_tree_sits() {
        assert_eq!(tree_root("", ".probe"), "'.probe/'");
        assert_eq!(
            tree_root("garage-server", "LinuxProbe"),
            "'garage-server/LinuxProbe/' (computerName/module changed from the defaults; not \
             recommended)"
        );
        assert_eq!(
            tree_root("", ""),
            "the product root (computerName/module changed from the defaults; not recommended)"
        );
    }

    #[test]
    fn version_comments_match_the_managed_product_version_sensor() {
        // Captured from the managed collector on Linux: "Start: 22/09/2026 14:45:12" and
        // "Stop: 22/09/2026 14:51:04".
        assert_eq!(
            version_comment(VersionEvent::Start, "22/09/2026 14:45:12"),
            "Start: 22/09/2026 14:45:12"
        );
        assert_eq!(
            version_comment(VersionEvent::Stop, "22/09/2026 14:51:04"),
            "Stop: 22/09/2026 14:51:04"
        );
    }

    #[test]
    fn versions_parse_like_the_collector() {
        assert_eq!(parse_version("0.1.0"), (0, 1, Some(0), None));
        assert_eq!(parse_version("1.2.3.4"), (1, 2, Some(3), Some(4)));
        assert_eq!(parse_version("7"), (7, 0, None, None));
        assert_eq!(parse_version(""), (0, 0, None, None));
    }

    /// Parity-audit capture, not part of the normal suite: runs the probe's exact registration
    /// against a fake Sensor API so its `/commands` and `/list` traffic can be diffed against the
    /// managed collector's (README "Parity contract"). Run with
    /// `HSM_PARITY_ADDRESS=http://<host> HSM_PARITY_PORT=<port> HSM_PARITY_SECONDS=<n>
    /// cargo test --features linux-default-sensors -- --ignored parity_capture`.
    #[test]
    #[ignore = "audit tool: needs a capture server on HSM_PARITY_PORT"]
    fn parity_capture() {
        let port: u16 = std::env::var("HSM_PARITY_PORT")
            .expect("HSM_PARITY_PORT")
            .parse()
            .expect("port");
        let seconds: u64 = std::env::var("HSM_PARITY_SECONDS")
            .ok()
            .and_then(|value| value.parse().ok())
            .unwrap_or(70);

        let address =
            std::env::var("HSM_PARITY_ADDRESS").unwrap_or_else(|_| "http://127.0.0.1".into());
        let mut options = CollectorOptions::new("native-parity-key", address, port);
        options.allow_plaintext_transport = true;
        options.module = Some(crate::config::DEFAULT_MODULE.into());
        let collector = Collector::new(&options).expect("create");
        let logger = Arc::new(Logger::new(Level::Debug, None));
        let sink = Arc::clone(&logger);
        collector
            .set_logger(move |level, message| {
                sink.log(logging::collector_message_level(level, message), message)
            })
            .expect("logger");
        collector.use_http_transport().expect("transport");

        let version = register_sensors(&collector, &logger);
        collector.start().expect("start");
        if let Some(sensor) = &version {
            post_product_version(sensor, VersionEvent::Start, &logger);
        }
        std::thread::sleep(Duration::from_secs(seconds));
        if let Some(sensor) = &version {
            post_product_version(sensor, VersionEvent::Stop, &logger);
        }
        collector.stop().expect("stop");
    }
}
