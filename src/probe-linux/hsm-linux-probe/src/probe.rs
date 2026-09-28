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

    logger.info(format!(
        "starting: collector {} -> {}:{} (module '{}', computer '{}')",
        hsm_collector::library_version_string(),
        config.hsm.address,
        config.hsm.port,
        config.hsm.module,
        if config.hsm.computer_name.is_empty() {
            "<auto>"
        } else {
            config.hsm.computer_name.as_str()
        }
    ));

    collector.start()?;

    // After Start: the collector drops a value posted before it can accept data.
    if let Some(sensor) = &product_version {
        post_product_version(sensor, VersionEvent::Start, &logger);
    }

    // The shared catalog is sampled, queued and sent by the collector's own threads. Each
    // probe-only source gets a thread of its own (so one hung read cannot stall another), started
    // only now: a value posted before Start would be dropped. The main thread only waits, and the
    // scope joins the source threads before the collector is stopped.
    let stop_sources = StopSignal::new();
    let source_count = probe_only_sources.len();
    std::thread::scope(|scope| {
        for source in probe_only_sources.iter_mut() {
            let (stop, logger) = (&stop_sources, &logger);
            let name = source.name();
            let spawned = std::thread::Builder::new()
                .name(format!("probe-{}", name.replace(' ', "-")))
                .spawn_scoped(scope, move || {
                    probe_only::run_source(source.as_mut(), stop, logger)
                });
            if let Err(error) = spawned {
                logger.error(format!(
                    "cannot start the '{name}' probe-only source thread: {error}"
                ));
            }
        }
        if source_count > 0 {
            logger.info(format!("{source_count} probe-only source(s) running"));
        }

        while !shutdown::is_requested() {
            std::thread::sleep(TICK);
        }
        stop_sources.request();
    });

    // Before Stop, so the value is still accepted and goes out with the stop drain — as the
    // managed ProductVersionSensor.StopAsync does.
    if let Some(sensor) = &product_version {
        post_product_version(sensor, VersionEvent::Stop, &logger);
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

    Ok(())
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

    let mut options =
        CollectorOptions::new(key.expose(), config.hsm.address.trim(), config.hsm.port);
    options.module = Some(config.hsm.module.clone());
    if !config.hsm.computer_name.is_empty() {
        options.computer_name = Some(config.hsm.computer_name.clone());
    }
    options.package_collect_period =
        Some(Duration::from_secs(config.hsm.package_collect_period_sec));
    options.request_timeout = Some(Duration::from_secs(config.hsm.request_timeout_sec));

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
        options.computer_name = Some("garage-server".into());
        options.module = Some("LinuxProbe".into());
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
    /// probe-only set against a fake host (a coretemp package sensor and a mounted target) when
    /// `with_probe_only` is set, so the result does not depend on the machine running the test.
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
            tree.file(
                "mountinfo",
                "22 1 8:33 / / rw,relatime shared:1 - ext4 /dev/sdc1 rw\n",
            );
            Self { tree }
        }

        fn environment(&self) -> HostEnvironment {
            HostEnvironment {
                sys_root: self.tree.0.join("sys"),
                mountinfo: self.tree.0.join("mountinfo"),
                disk_target: self.tree.0.clone(),
                statvfs: |_| {
                    Ok(crate::probe_only::disk::FsStats {
                        blocks: 1000,
                        blocks_available: 400,
                        files: 100,
                        files_available: 90,
                    })
                },
                online_cpus: || Ok(4),
            }
        }
    }

    /// Sensors that exist only in this probe (README "Probe-only sensors"; owner decisions of
    /// 2026-09-24): computer-level, so they sit under `<computer>/.computer/`, not the module.
    const PROBE_ONLY_SET: &[&str] = &[
        "garage-server/.computer/CPU temperature",
        "garage-server/.computer/Disks monitoring/Free inodes %",
        "garage-server/.computer/Disks monitoring/Free space on disk %",
        "garage-server/.computer/Logical cores",
    ];

    /// The module set: managed `AddAllModuleSensors` minus `Process ThreadPool thread count`
    /// (a CLR concept) and minus the metric-fed process sensors, which register only when
    /// the build can feed them (`METRIC_FED_SET`).
    const MODULE_SET: &[&str] = &[
        "garage-server/LinuxProbe/.module/Collector errors",
        "garage-server/LinuxProbe/.module/Collector queue stats/Items count in package",
        "garage-server/LinuxProbe/.module/Collector queue stats/Package content size",
        "garage-server/LinuxProbe/.module/Collector queue stats/Package process time",
        "garage-server/LinuxProbe/.module/Collector queue stats/Queue overflow",
        "garage-server/LinuxProbe/.module/Collector version",
        "garage-server/LinuxProbe/.module/Service alive",
        "garage-server/LinuxProbe/.module/Version",
    ];

    /// Everything the metric-source factory feeds: the computer set (managed
    /// `AddAllComputerSensors` on Unix) plus the process sensors. Registered only when the build
    /// has the Linux metric sources (#1414); without them these would be empty nodes.
    #[cfg(feature = "linux-default-sensors")]
    const METRIC_FED_SET: &[&str] = &[
        "garage-server/.computer/Disks monitoring/Free space on disk",
        "garage-server/.computer/Disks monitoring/Free space on disk prediction",
        "garage-server/.computer/Free RAM memory",
        "garage-server/.computer/Total CPU",
        "garage-server/LinuxProbe/.module/Process process/Process CPU",
        "garage-server/LinuxProbe/.module/Process process/Process memory",
        "garage-server/LinuxProbe/.module/Process process/Process thread count",
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
        expected.sort_unstable();
        let registered = registered_paths_with(&ProbeConfig::default(), true);
        assert_eq!(registered, expected);

        let parity = parity_set();
        assert!(
            PROBE_ONLY_SET.iter().all(|path| !parity.contains(path)),
            "a probe-only sensor must never shadow a parity sensor"
        );
    }

    #[test]
    fn host_sensors_switch_off_as_configured() {
        let mut config = ProbeConfig::default();
        config.host_sensors.enabled = false;
        let mut parity = parity_set();
        parity.sort_unstable();
        assert_eq!(registered_paths_with(&config, true), parity);

        let mut config = ProbeConfig::default();
        config.host_sensors.cpu_temperature = false;
        config.host_sensors.disk = false;
        let mut expected = parity_set();
        expected.push("garage-server/.computer/Logical cores");
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
            &find("garage-server/.computer/Logical cores"),
            &[
                "\"SensorType\":1,",
                "\"TTLTicks\":[1728000000000]",
                "\"Alerts\":null",
                "\"IsSingletonSensor\":true",
            ],
        );
        // DoubleBar, no unit (°C has no code), Mean > 80 warning band + Mean > 90 error.
        contains_all(
            &find("garage-server/.computer/CPU temperature"),
            &[
                "\"SensorType\":5,",
                "\"OriginalUnit\":null",
                "{\"Combination\":0,\"Operation\":2,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"80\"}},\
                 {\"Combination\":0,\"Operation\":0,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"90\"}}],\
                 \"Status\":1,",
                "\"Conditions\":[{\"Combination\":0,\"Operation\":2,\"Property\":103,\
                 \"Target\":{\"Type\":0,\"Value\":\"90\"}}],\"Status\":3,",
                "\"ScheduledRepeatMode\":20,\"ScheduledInstantSend\":true",
            ],
        );
        // Double, Percents, < 10 warning band + < 5 error.
        contains_all(
            &find("garage-server/.computer/Disks monitoring/Free space on disk %"),
            &[
                "\"SensorType\":2,",
                "\"OriginalUnit\":100,",
                "{\"Combination\":0,\"Operation\":1,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"10\"}},\
                 {\"Combination\":0,\"Operation\":3,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"5\"}}],\
                 \"Status\":1,",
                "\"Conditions\":[{\"Combination\":0,\"Operation\":1,\"Property\":20,\
                 \"Target\":{\"Type\":0,\"Value\":\"5\"}}],\"Status\":3,",
            ],
        );
        // Double, Percents, < 10 warning only.
        let inodes = find("garage-server/.computer/Disks monitoring/Free inodes %");
        contains_all(
            &inodes,
            &[
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
        // the same fixed ".module/Process process" node, so one HSM alert template on that path
        // applies to all of them. A per-process name would silently detach hosts from it.
        let paths = registered_paths();
        for sensor in ["Process CPU", "Process memory", "Process thread count"] {
            let expected = format!("garage-server/LinuxProbe/.module/Process process/{sensor}");
            assert!(
                paths.iter().any(|path| path == &expected),
                "missing {expected} in {paths:#?}"
            );
        }
        assert!(
            paths
                .iter()
                .filter(|path| path.contains("/.module/Process "))
                .all(|path| path.contains("/.module/Process process/")),
            "no other process node may be registered: {paths:#?}"
        );
        assert!(
            paths.iter().all(|path| !path.contains("ThreadPool")),
            "a native process has no CLR thread pool: {paths:#?}"
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
        options.computer_name = Some("garage-server".into());
        options.module = Some("LinuxProbe".into());
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
