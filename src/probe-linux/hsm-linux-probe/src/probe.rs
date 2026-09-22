//! Collector wiring and the sampling loop.
//!
//! The lifecycle order mirrors `src/agent/src/agent_runtime.cpp`, the other native host of this
//! collector: build options -> install the log sink -> select the transport -> install the metric
//! sources -> register sensors -> start. Everything after that is the collector's job; the probe
//! only feeds it the signals that exist in no collector (initiative §4.2).

use std::sync::Arc;
use std::time::{Duration, Instant};

use hsm_collector::{
    Collector, CollectorOptions, DefaultSensor, EnumOption, EnumSensor, Error as CollectorError,
    LogLevel, SensorOptions, SensorStatus, VersionSensor, LINUX_METRIC_SOURCES_AVAILABLE,
};

use crate::config::Config;
use crate::logging::{self, Logger};
use crate::procfs;
use crate::secret::{self, Secret};
use crate::shutdown;

/// Version reported as the probe's own `.module/Version` sensor.
pub const PROBE_VERSION: &str = env!("CARGO_PKG_VERSION");

/// How often the loop wakes to re-check the stop flag. Bounds the SIGTERM response time.
const TICK: Duration = Duration::from_millis(200);

const LOAD_AVERAGE_1M: &str = "CPU/Load average 1m";
const LOAD_AVERAGE_5M: &str = "CPU/Load average 5m";
const LOAD_AVERAGE_15M: &str = "CPU/Load average 15m";
const LOGICAL_CORES: &str = "CPU/Logical cores";
const LOADAVG_SOURCE_STATUS: &str = "Probe/Sources/loadavg status";
const CORES_SOURCE_STATUS: &str = "Probe/Sources/logical-cores status";

/// Per-source health, made visible so a failing source is never indistinguishable from a quiet one
/// (initiative §4, "failure isolation made visible").
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum SourceStatus {
    Ok = 0,
    /// Partial data. Registered now so the option set is stable for the sources that will report
    /// it (Docker, #1416); neither source in this slice can be partially healthy.
    Degraded = 1,
    Failed = 2,
}

fn source_status_options() -> Vec<EnumOption> {
    vec![
        EnumOption::new(SourceStatus::Ok as i32, "ok"),
        EnumOption::new(SourceStatus::Degraded as i32, "degraded"),
        EnumOption::new(SourceStatus::Failed as i32, "failed"),
    ]
}

fn post_source_status(
    sensor: &EnumSensor<'_>,
    status: SourceStatus,
    comment: Option<&str>,
    logger: &Logger,
) {
    let sensor_status = match status {
        SourceStatus::Ok => SensorStatus::Ok,
        SourceStatus::Degraded => SensorStatus::Warning,
        SourceStatus::Failed => SensorStatus::Error,
    };
    if let Err(error) = sensor.add_with(status as i32, sensor_status, comment) {
        logger.error(format!("cannot post a source status: {error}"));
    }
}

/// Build, start and run the probe until a stop is requested.
pub fn run(config: &Config, logger: Arc<Logger>) -> Result<(), Box<dyn std::error::Error>> {
    let collector = build_collector(config, Arc::clone(&logger))?;

    register_default_sensors(&collector, &logger);
    let process_name = procfs::current_process_name();
    match &process_name {
        Some(name) => logger.info(format!(
            "process sensors register under '.module/Process {name}'"
        )),
        None => logger
            .warn("cannot read /proc/self/comm; the process node falls back to 'Process process'"),
    }
    let product_version = register_module_sensors(&collector, process_name.as_deref(), &logger);

    // TTLs are derived from the sampling periods so a reconfigured period cannot leave a sensor
    // permanently expired: 3x the period for the fast source (the §4.2 value at the default 60 s),
    // 2x for the daily one (48 h at the default).
    let load_ttl = config.load_average_period() * 3;
    let cores_ttl = config.logical_cores_period() * 2;

    let load_1m = collector.double_sensor(
        LOAD_AVERAGE_1M,
        &SensorOptions::default()
            .with_ttl(load_ttl)
            .with_description("1-minute kernel load average from /proc/loadavg."),
    )?;
    let load_5m = collector.double_sensor(
        LOAD_AVERAGE_5M,
        &SensorOptions::default()
            .with_ttl(load_ttl)
            .with_description("5-minute kernel load average from /proc/loadavg."),
    )?;
    let load_15m = collector.double_sensor(
        LOAD_AVERAGE_15M,
        &SensorOptions::default()
            .with_ttl(load_ttl)
            .with_description("15-minute kernel load average from /proc/loadavg."),
    )?;
    let cores = collector.int_sensor(
        LOGICAL_CORES,
        &SensorOptions::default()
            .with_ttl(cores_ttl)
            .with_description("Logical CPUs the host exposes, counted from /proc/cpuinfo."),
    )?;
    let loadavg_status = collector.enum_sensor(
        LOADAVG_SOURCE_STATUS,
        Some("Health of the probe's /proc/loadavg source."),
        &source_status_options(),
    )?;
    let cores_status = collector.enum_sensor(
        CORES_SOURCE_STATUS,
        Some("Health of the probe's /proc/cpuinfo source."),
        &source_status_options(),
    )?;

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
        post_product_version(sensor, &logger);
    }

    // Both sources fire immediately on start, then on their own period.
    let mut next_load = Instant::now();
    let mut next_cores = Instant::now();

    while !shutdown::is_requested() {
        let now = Instant::now();

        if now >= next_load {
            match procfs::read_loadavg() {
                Ok(load) => {
                    let mut failures = Vec::new();
                    for (sensor, value, name) in [
                        (&load_1m, load.one, LOAD_AVERAGE_1M),
                        (&load_5m, load.five, LOAD_AVERAGE_5M),
                        (&load_15m, load.fifteen, LOAD_AVERAGE_15M),
                    ] {
                        if let Err(error) = sensor.add(value) {
                            failures.push(format!("{name}: {error}"));
                        }
                    }
                    if failures.is_empty() {
                        logger.debug(format!(
                            "load average {:.2} {:.2} {:.2}",
                            load.one, load.five, load.fifteen
                        ));
                        post_source_status(&loadavg_status, SourceStatus::Ok, None, &logger);
                    } else {
                        let detail = failures.join("; ");
                        logger.error(format!("cannot post load averages: {detail}"));
                        post_source_status(
                            &loadavg_status,
                            SourceStatus::Failed,
                            Some(&detail),
                            &logger,
                        );
                    }
                }
                Err(error) => {
                    // Skip the value entirely: a substitute zero would read as an idle host.
                    let detail = error.to_string();
                    logger.error(format!("load average source failed: {detail}"));
                    post_source_status(
                        &loadavg_status,
                        SourceStatus::Failed,
                        Some(&detail),
                        &logger,
                    );
                }
            }
            next_load = now + config.load_average_period();
        }

        if now >= next_cores {
            match procfs::read_logical_cores() {
                Ok(count) => {
                    if let Err(error) = cores.add(count) {
                        let detail = error.to_string();
                        logger.error(format!("cannot post the logical core count: {detail}"));
                        post_source_status(
                            &cores_status,
                            SourceStatus::Failed,
                            Some(&detail),
                            &logger,
                        );
                    } else {
                        post_source_status(&cores_status, SourceStatus::Ok, None, &logger);
                    }
                }
                Err(error) => {
                    let detail = error.to_string();
                    logger.error(format!("logical core source failed: {detail}"));
                    post_source_status(&cores_status, SourceStatus::Failed, Some(&detail), &logger);
                }
            }
            next_cores = now + config.logical_cores_period();
        }

        let until_next = next_load
            .min(next_cores)
            .saturating_duration_since(Instant::now());
        std::thread::sleep(TICK.min(until_next).max(Duration::from_millis(1)));
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

/// Enable the collector's own catalog: the platform-free module sensors always, and the host
/// catalog only when this build can actually feed it live values.
fn register_default_sensors(collector: &Collector, logger: &Logger) {
    match collector.install_linux_metric_sources() {
        Ok(()) => logger.info("Linux metric sources installed; the default host catalog is live"),
        Err(CollectorError::Unsupported { .. }) => logger.error(
            "the collector's Linux metric sources are unavailable (built without the \
             'linux-default-sensors' feature — see #1414): the default host catalog \
             (Total CPU, Free RAM, free disk, process counters) will NOT be reported; \
             the probe continues with its own sensors only",
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

/// Register the module group — process sensors, collector self-sensors, queue diagnostics and the
/// product version — and return the product-version handle for [`post_product_version`].
///
/// Deliberately not `add_all_module_sensors`: that group helper cannot carry a process name, so
/// the collector names the node with its `Process process` placeholder. The managed collector names
/// it `Process <ProcessName>` (`ProcessCollectionPrototypes`), so the probe registers the process
/// sensors one by one with the name .NET would use. `Process ThreadPool thread count` is left out
/// on purpose, as in `src/agent`: it is a CLR concept a native process can only report as 0.
fn register_module_sensors<'c>(
    collector: &'c Collector,
    process_name: Option<&str>,
    logger: &Logger,
) -> Option<VersionSensor<'c>> {
    for sensor in [
        DefaultSensor::ProcessCpu,
        DefaultSensor::ProcessMemory,
        DefaultSensor::ProcessThreadCount,
    ] {
        if let Err(error) = collector.add_default_sensor(sensor, process_name) {
            logger.error(format!("cannot register {sensor:?}: {error}"));
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

/// Post the probe's version once, with the start time, as the collector's own
/// `add_all_module_sensors` does for a host (managed `ProductVersionSensor.StartAsync`).
fn post_product_version(sensor: &VersionSensor<'_>, logger: &Logger) {
    let (major, minor, build, revision) = parse_version(PROBE_VERSION);
    let comment = format!("Start: {}", logging::utc_iso8601_now());
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
    use crate::logging::Level;

    /// The registration text the collector sends to `/commands` for the module group, with the
    /// computer/module prefixes the trial host uses.
    fn module_registrations(process_name: Option<&str>) -> Vec<String> {
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        options.computer_name = Some("garage-server".into());
        options.module = Some("LinuxProbe".into());
        let collector = Collector::new(&options).expect("create");
        let logger = Logger::new(Level::Error, None);

        let version = register_module_sensors(&collector, process_name, &logger);
        assert!(
            version.is_some(),
            "the product version sensor must register"
        );
        // The collector records the registration payloads at Start — the /commands batch. No
        // transport is installed, so the in-memory sender receives it and nothing leaves the test.
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        assert!(
            !registrations.is_empty(),
            "Start must record the registrations"
        );
        registrations
    }

    fn registered_paths(registrations: &[String]) -> Vec<String> {
        registrations
            .iter()
            .filter_map(|json| {
                let start = json.find("\"Path\":\"")? + "\"Path\":\"".len();
                let end = json[start..].find('"')? + start;
                Some(json[start..end].to_string())
            })
            .collect()
    }

    #[test]
    fn the_process_node_is_named_after_the_probe_binary() {
        // The trial host showed "Process process/…": the group helper's placeholder. This pins
        // the node the managed collector would produce for a process named hsm-linux-probe.
        let paths = registered_paths(&module_registrations(Some("hsm-linux-probe")));
        for sensor in ["Process CPU", "Process memory", "Process thread count"] {
            let expected =
                format!("garage-server/LinuxProbe/.module/Process hsm-linux-probe/{sensor}");
            assert!(
                paths.iter().any(|path| path == &expected),
                "missing {expected} in {paths:#?}"
            );
        }
        assert!(
            paths.iter().all(|path| !path.contains("Process process")),
            "the placeholder node must not be registered: {paths:#?}"
        );
        assert!(
            paths.iter().all(|path| !path.contains("ThreadPool")),
            "a native process has no CLR thread pool: {paths:#?}"
        );
    }

    #[test]
    fn the_rest_of_the_module_group_is_still_registered() {
        let paths = registered_paths(&module_registrations(Some("hsm-linux-probe")));
        for sensor in [
            ".module/Service alive",
            ".module/Collector version",
            ".module/Version",
        ] {
            assert!(
                paths.iter().any(|path| path.ends_with(sensor)),
                "missing {sensor} in {paths:#?}"
            );
        }
        assert!(
            paths
                .iter()
                .any(|path| path.contains(".module/Collector queue stats/")),
            "missing the queue diagnostics in {paths:#?}"
        );
    }

    #[test]
    fn versions_parse_like_the_collector() {
        assert_eq!(parse_version("0.1.0"), (0, 1, Some(0), None));
        assert_eq!(parse_version("1.2.3.4"), (1, 2, Some(3), Some(4)));
        assert_eq!(parse_version("7"), (7, 0, None, None));
        assert_eq!(parse_version(""), (0, 0, None, None));
    }

    #[test]
    fn source_status_keys_are_the_registered_enum_options() {
        let options = source_status_options();
        assert_eq!(options.len(), 3);
        assert_eq!(options[0].key, SourceStatus::Ok as i32);
        assert_eq!(options[0].value, "ok");
        assert_eq!(options[1].key, SourceStatus::Degraded as i32);
        assert_eq!(options[2].key, SourceStatus::Failed as i32);
        assert_eq!(options[2].value, "failed");
    }

    #[test]
    fn sensor_paths_are_stable() {
        // The tree shape is an operator-visible contract (alert templates hang off these paths),
        // so a rename must be a deliberate edit here.
        assert_eq!(LOAD_AVERAGE_1M, "CPU/Load average 1m");
        assert_eq!(LOGICAL_CORES, "CPU/Logical cores");
        assert_eq!(LOADAVG_SOURCE_STATUS, "Probe/Sources/loadavg status");
    }
}
