//! Host sensors: `.computer/Logical cores` and `.computer/CPU temperature`.
//!
//! # CPU temperature source rule (deterministic, first match wins)
//!
//! 1. hwmon `coretemp`: the `temp<N>_input` whose `temp<N>_label` is exactly `Package id 0`
//!    (the whole-package reading on Intel; lowest-numbered `hwmon*` first);
//! 2. else `/sys/class/thermal/thermal_zone*` whose `type` is `x86_pkg_temp` (the same package
//!    sensor through the thermal framework);
//! 3. else the first `thermal_zone*` whose `type` contains `cpu` (case-insensitive — e.g.
//!    `cpu-thermal` on ARM boards).
//!
//! Directories are visited in numeric order (`hwmon2` before `hwmon10`). Only the `name`/`type`
//! and `label` attributes of other devices are read, never their inputs: a `drivetemp` hwmon
//! input issues a SMART command, which would wake a sleeping disk. Values are millidegrees
//! Celsius. With no match the sensor is not registered at all (logged once at INFO), so a host
//! without a sensor gets no permanently empty node.

use std::path::{Path, PathBuf};
use std::time::Duration;

use hsm_collector::{
    instant_hourly_schedule_anchor, AlertCombination, AlertDestination, AlertIcon, AlertKind,
    AlertOperation, AlertProperty, AlertRepeat, AlertTarget, Collector, DoubleBarSensor, IntSensor,
    SensorOptions,
};

use super::{FailureLog, HostEnvironment, Source};
use crate::logging::Logger;

pub const LOGICAL_CORES_PATH: &str = ".computer/Logical cores";
pub const CPU_TEMPERATURE_PATH: &str = ".computer/CPU temperature";

/// Posted at start and then once a day; the TTL covers one missed day.
const LOGICAL_CORES_PERIOD: Duration = Duration::from_secs(24 * 3600);
const LOGICAL_CORES_TTL: Duration = Duration::from_secs(48 * 3600);

/// One sample every 5 s into a 5-minute bar: 60 samples per bar, one stored record per 5 minutes.
const TEMPERATURE_SAMPLE_PERIOD: Duration = Duration::from_secs(5);
const TEMPERATURE_BAR_PERIOD: Duration = Duration::from_secs(300);
/// Carried by the ABI for parity with the managed option; the collector does not post partial bars
/// of a custom bar, so the stored record is the closed 5-minute bar.
const TEMPERATURE_POST_PERIOD: Duration = Duration::from_secs(15);
const TEMPERATURE_PRECISION: i32 = 1;

/// Mean over a bar above which the alerts fire, °C.
const TEMPERATURE_WARNING_ABOVE: &str = "80";
const TEMPERATURE_ERROR_ABOVE: &str = "90";

/// `sysconf(_SC_NPROCESSORS_ONLN)` — what `nproc` reports without an affinity mask.
pub fn online_cpus() -> std::io::Result<i32> {
    #[cfg(unix)]
    {
        // SAFETY: sysconf reads a system constant; no pointers.
        let count = unsafe { libc::sysconf(libc::_SC_NPROCESSORS_ONLN) };
        if count > 0 {
            Ok(i32::try_from(count).unwrap_or(i32::MAX))
        } else {
            Err(std::io::Error::last_os_error())
        }
    }
    #[cfg(not(unix))]
    {
        Err(std::io::Error::new(
            std::io::ErrorKind::Unsupported,
            "sysconf is Linux-only",
        ))
    }
}

pub fn register_logical_cores<'c>(
    collector: &'c Collector,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    let options = SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_ttl(LOGICAL_CORES_TTL)
        .with_description(
            "Number of online logical CPUs (sysconf(_SC_NPROCESSORS_ONLN), what `nproc` shows). \
             Posted at start and once a day. Read container CPU percentages against it.",
        );
    match collector.int_sensor(LOGICAL_CORES_PATH, &options) {
        Ok(sensor) => Some(Box::new(LogicalCores {
            sensor,
            read: environment.online_cpus,
            failures: FailureLog::default(),
        })),
        Err(error) => {
            logger.error(format!("cannot register {LOGICAL_CORES_PATH}: {error}"));
            None
        }
    }
}

struct LogicalCores<'c> {
    sensor: IntSensor<'c>,
    read: fn() -> std::io::Result<i32>,
    failures: FailureLog,
}

impl Source for LogicalCores<'_> {
    fn name(&self) -> &'static str {
        "logical cores"
    }

    fn period(&self) -> Duration {
        LOGICAL_CORES_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        match (self.read)() {
            Ok(count) => {
                self.failures.succeeded(logger, LOGICAL_CORES_PATH);
                if let Err(error) = self.sensor.add(count) {
                    logger.error(format!("cannot post {LOGICAL_CORES_PATH}: {error}"));
                }
            }
            Err(error) => {
                self.failures.failed(
                    logger,
                    LOGICAL_CORES_PATH,
                    &format!("sysconf(_SC_NPROCESSORS_ONLN) failed: {error}"),
                );
            }
        }
    }
}

/// The resolved temperature input and a human description of where it came from.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct TemperatureInput {
    pub path: PathBuf,
    pub origin: String,
}

/// Apply the source rule (module docs) under `sys_root`.
pub fn find_cpu_temperature(sys_root: &Path) -> Option<TemperatureInput> {
    // 1. hwmon coretemp "Package id 0".
    for hwmon in numbered_entries(&sys_root.join("class/hwmon"), "hwmon") {
        if read_trimmed(&hwmon.join("name")).as_deref() != Some("coretemp") {
            continue;
        }
        for label in numbered_files(&hwmon, "temp", "_label") {
            if read_trimmed(&label).as_deref() == Some("Package id 0") {
                let input = PathBuf::from(
                    label
                        .to_string_lossy()
                        .trim_end_matches("_label")
                        .to_string()
                        + "_input",
                );
                return Some(TemperatureInput {
                    origin: format!("hwmon coretemp 'Package id 0' ({})", input.display()),
                    path: input,
                });
            }
        }
    }

    let zones = numbered_entries(&sys_root.join("class/thermal"), "thermal_zone");
    // 2. thermal zone x86_pkg_temp.
    for zone in &zones {
        if read_trimmed(&zone.join("type")).as_deref() == Some("x86_pkg_temp") {
            return Some(TemperatureInput {
                path: zone.join("temp"),
                origin: format!(
                    "thermal zone x86_pkg_temp ({})",
                    zone.join("temp").display()
                ),
            });
        }
    }
    // 3. first cpu-like thermal zone.
    for zone in &zones {
        if let Some(kind) = read_trimmed(&zone.join("type")) {
            if kind.to_ascii_lowercase().contains("cpu") {
                return Some(TemperatureInput {
                    path: zone.join("temp"),
                    origin: format!("thermal zone {kind} ({})", zone.join("temp").display()),
                });
            }
        }
    }
    None
}

pub fn register_cpu_temperature<'c>(
    collector: &'c Collector,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    let Some(input) = find_cpu_temperature(&environment.sys_root) else {
        logger.info(format!(
            "{CPU_TEMPERATURE_PATH} is not registered: no CPU temperature source on this host \
             (looked for hwmon coretemp 'Package id 0', thermal zone x86_pkg_temp, a cpu thermal \
             zone)"
        ));
        return None;
    };
    logger.info(format!("{CPU_TEMPERATURE_PATH}: reading {}", input.origin));

    let options = SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_description(format!(
            "CPU package temperature in °C (the collector has no temperature unit): a 5-minute bar \
             of one sample every 5 s. Source on this host: {}.",
            input.origin
        ));
    let sensor = match collector.double_bar_sensor(
        CPU_TEMPERATURE_PATH,
        TEMPERATURE_BAR_PERIOD,
        TEMPERATURE_POST_PERIOD,
        TEMPERATURE_PRECISION,
        &options,
    ) {
        Ok(sensor) => sensor,
        Err(error) => {
            logger.error(format!("cannot register {CPU_TEMPERATURE_PATH}: {error}"));
            return None;
        }
    };
    for (band, alert) in [
        ("warning", temperature_warning_alert(collector)),
        ("error", temperature_error_alert(collector)),
    ] {
        let attached = alert.and_then(|alert| sensor.attach_alert(&alert));
        if let Err(error) = attached {
            logger.error(format!(
                "cannot attach the {band} alert to {CPU_TEMPERATURE_PATH}: {error}"
            ));
        }
    }

    Some(Box::new(CpuTemperature {
        sensor,
        input,
        failures: FailureLog::default(),
    }))
}

/// Mean in (80, 90] °C: a notification with the warning icon. HSM alerts cannot raise a Warning
/// status (the server only offers "set Error"), so "warning" is the icon + message, exactly like the
/// managed Total CPU / Free RAM alerts.
fn temperature_warning_alert(
    collector: &Collector,
) -> hsm_collector::Result<hsm_collector::Alert<'_>> {
    Ok(collector
        .alert(AlertKind::Bar)?
        .condition(
            AlertCombination::And,
            AlertProperty::Mean,
            AlertOperation::GreaterThan,
            AlertTarget::Const(TEMPERATURE_WARNING_ABOVE.into()),
        )?
        .condition(
            AlertCombination::And,
            AlertProperty::Mean,
            AlertOperation::LessThanOrEqual,
            AlertTarget::Const(TEMPERATURE_ERROR_ABOVE.into()),
        )?
        .scheduled_notification(
            "[$product]$path $property $operation $target °C",
            instant_hourly_schedule_anchor(),
            AlertRepeat::Hourly,
            true,
            AlertDestination::FromParent,
        )?
        .icon(AlertIcon::Warning)?
        .build())
}

/// Mean above 90 °C: notification + the sensor raised to Error.
fn temperature_error_alert(
    collector: &Collector,
) -> hsm_collector::Result<hsm_collector::Alert<'_>> {
    Ok(collector
        .alert(AlertKind::Bar)?
        .condition(
            AlertCombination::And,
            AlertProperty::Mean,
            AlertOperation::GreaterThan,
            AlertTarget::Const(TEMPERATURE_ERROR_ABOVE.into()),
        )?
        .scheduled_notification(
            "[$product]$path $property $operation $target °C",
            instant_hourly_schedule_anchor(),
            AlertRepeat::Hourly,
            true,
            AlertDestination::FromParent,
        )?
        .icon(AlertIcon::Error)?
        .sensor_error()?
        .build())
}

struct CpuTemperature<'c> {
    sensor: DoubleBarSensor<'c>,
    input: TemperatureInput,
    failures: FailureLog,
}

impl Source for CpuTemperature<'_> {
    fn name(&self) -> &'static str {
        "cpu temperature"
    }

    fn period(&self) -> Duration {
        TEMPERATURE_SAMPLE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        match read_celsius(&self.input.path) {
            Ok(celsius) => {
                self.failures.succeeded(logger, CPU_TEMPERATURE_PATH);
                if let Err(error) = self.sensor.add(celsius) {
                    logger.error(format!("cannot post {CPU_TEMPERATURE_PATH}: {error}"));
                }
            }
            Err(reason) => self.failures.failed(logger, CPU_TEMPERATURE_PATH, &reason),
        }
    }
}

/// Read a sysfs millidegree value as °C. Any failure — unreadable, not an integer — is an error,
/// never a 0 °C sample.
pub fn read_celsius(path: &Path) -> Result<f64, String> {
    let text = std::fs::read_to_string(path)
        .map_err(|error| format!("cannot read {}: {error}", path.display()))?;
    let millidegrees: i64 = text.trim().parse().map_err(|_| {
        format!(
            "{} does not hold millidegrees: '{}'",
            path.display(),
            text.trim()
        )
    })?;
    Ok(millidegrees as f64 / 1000.0)
}

fn read_trimmed(path: &Path) -> Option<String> {
    std::fs::read_to_string(path)
        .ok()
        .map(|text| text.trim().to_string())
}

/// `<dir>/<prefix><N>` entries, sorted by N.
fn numbered_entries(dir: &Path, prefix: &str) -> Vec<PathBuf> {
    let mut entries: Vec<(u32, PathBuf)> = std::fs::read_dir(dir)
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|entry| {
            let name = entry.file_name();
            let number = name.to_str()?.strip_prefix(prefix)?.parse().ok()?;
            Some((number, entry.path()))
        })
        .collect();
    entries.sort();
    entries.into_iter().map(|(_, path)| path).collect()
}

/// `<dir>/<prefix><N><suffix>` files, sorted by N.
fn numbered_files(dir: &Path, prefix: &str, suffix: &str) -> Vec<PathBuf> {
    let mut entries: Vec<(u32, PathBuf)> = std::fs::read_dir(dir)
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|entry| {
            let name = entry.file_name();
            let number = name
                .to_str()?
                .strip_prefix(prefix)?
                .strip_suffix(suffix)?
                .parse()
                .ok()?;
            Some((number, entry.path()))
        })
        .collect();
    entries.sort();
    entries.into_iter().map(|(_, path)| path).collect()
}

#[cfg(test)]
pub mod tests {
    use super::*;
    use std::fs;

    /// A throwaway directory under the system temp dir, removed on drop.
    pub struct FakeTree(pub PathBuf);

    impl FakeTree {
        pub fn new(tag: &str) -> Self {
            use std::sync::atomic::{AtomicUsize, Ordering};
            static NEXT: AtomicUsize = AtomicUsize::new(0);
            let path = std::env::temp_dir().join(format!(
                "hsm-probe-{tag}-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::SeqCst)
            ));
            let _ = fs::remove_dir_all(&path);
            fs::create_dir_all(&path).expect("temp dir");
            Self(path)
        }

        pub fn file(&self, relative: &str, text: &str) {
            let path = self.0.join(relative);
            fs::create_dir_all(path.parent().unwrap()).expect("mkdir");
            fs::write(path, text).expect("write");
        }
    }

    impl Drop for FakeTree {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn coretemp_package_wins_over_thermal_zones() {
        let tree = FakeTree::new("coretemp");
        tree.file("class/thermal/thermal_zone0/type", "x86_pkg_temp\n");
        tree.file("class/thermal/thermal_zone0/temp", "40000\n");
        tree.file("class/hwmon/hwmon0/name", "acpitz\n");
        tree.file("class/hwmon/hwmon0/temp1_input", "99000\n");
        tree.file("class/hwmon/hwmon2/name", "coretemp\n");
        tree.file("class/hwmon/hwmon2/temp2_label", "Core 0\n");
        tree.file("class/hwmon/hwmon2/temp2_input", "45000\n");
        tree.file("class/hwmon/hwmon2/temp1_label", "Package id 0\n");
        tree.file("class/hwmon/hwmon2/temp1_input", "47000\n");

        let found = find_cpu_temperature(&tree.0).expect("found");
        assert_eq!(found.path, tree.0.join("class/hwmon/hwmon2/temp1_input"));
        assert_eq!(read_celsius(&found.path), Ok(47.0));
    }

    #[test]
    fn x86_pkg_temp_zone_is_the_fallback_then_a_cpu_zone() {
        let tree = FakeTree::new("zones");
        tree.file("class/thermal/thermal_zone0/type", "acpitz\n");
        tree.file("class/thermal/thermal_zone0/temp", "27800\n");
        tree.file("class/thermal/thermal_zone10/type", "x86_pkg_temp\n");
        tree.file("class/thermal/thermal_zone10/temp", "43000\n");
        tree.file("class/thermal/thermal_zone2/type", "cpu-thermal\n");
        tree.file("class/thermal/thermal_zone2/temp", "51500\n");
        let found = find_cpu_temperature(&tree.0).expect("found");
        assert_eq!(found.path, tree.0.join("class/thermal/thermal_zone10/temp"));

        let tree = FakeTree::new("cpu-zone");
        tree.file("class/thermal/thermal_zone0/type", "acpitz\n");
        tree.file("class/thermal/thermal_zone0/temp", "27800\n");
        tree.file("class/thermal/thermal_zone1/type", "CPU-therm\n");
        tree.file("class/thermal/thermal_zone1/temp", "51500\n");
        let found = find_cpu_temperature(&tree.0).expect("found");
        assert_eq!(found.path, tree.0.join("class/thermal/thermal_zone1/temp"));
        assert_eq!(read_celsius(&found.path), Ok(51.5));
    }

    #[test]
    fn no_cpu_source_means_no_sensor() {
        let tree = FakeTree::new("none");
        tree.file("class/thermal/thermal_zone0/type", "acpitz\n");
        tree.file("class/thermal/thermal_zone0/temp", "27800\n");
        tree.file("class/hwmon/hwmon0/name", "drivetemp\n");
        // A drivetemp input must never be read: it would wake a sleeping disk.
        tree.file("class/hwmon/hwmon0/temp1_input", "31000\n");
        assert_eq!(find_cpu_temperature(&tree.0), None);
        assert_eq!(find_cpu_temperature(&tree.0.join("missing")), None);
    }

    #[test]
    fn a_bad_reading_is_an_error_not_zero() {
        let tree = FakeTree::new("bad");
        tree.file("temp", "\n");
        assert!(read_celsius(&tree.0.join("temp")).is_err());
        tree.file("temp", "n/a\n");
        assert!(read_celsius(&tree.0.join("temp")).is_err());
        assert!(read_celsius(&tree.0.join("absent")).is_err());
    }
}
