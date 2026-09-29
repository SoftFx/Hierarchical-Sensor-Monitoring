//! Probe configuration: a JSON file holding placeholders only.
//!
//! Secrets never live here. The access key is read at startup from the file named by
//! `hsm.accessKeyFile` — under systemd that is the `LoadCredential=` drop in
//! `/run/credentials/hsm-linux-probe.service/`, root-owned and 0400 (initiative §4.3).
//!
//! Unknown keys are ignored, so a config written for an older probe keeps loading — e.g. the
//! `sampling` section, which configured probe-only sensors removed in the parity phase.

use std::fmt;
use std::path::{Path, PathBuf};
use std::time::Duration;

use serde::Deserialize;

/// Default location of the config file. Owned by the operator: the package seeds it once from
/// `/usr/share/hsm-linux-probe/config.example.json` when absent and never touches it on upgrade.
pub const DEFAULT_CONFIG_PATH: &str = "/etc/hsm-linux-probe/config.json";

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Config {
    pub hsm: HsmConfig,
    #[serde(default)]
    pub logging: LoggingConfig,
    /// Upper bound on the graceful stop. The collector's own drain is bounded too; this is the
    /// probe's guarantee that a `systemctl restart` is never held up.
    #[serde(default = "default_shutdown_timeout_sec")]
    pub shutdown_timeout_sec: u64,
    /// Probe-only sensor sources (the sensors that exist only in this probe, not in the shared
    /// collector catalog). Everything defaults to on.
    #[serde(default)]
    pub probe: ProbeConfig,
}

/// `probe.docker { enabled, socket, composeOnly, samplePeriodSec, oomLatchHours }`: the Docker
/// Compose source (#1416). Absent = defaults (enabled).
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DockerConfig {
    #[serde(default = "enabled_by_default")]
    pub enabled: bool,
    /// The Engine API socket.
    #[serde(default = "default_docker_socket")]
    pub socket: PathBuf,
    /// Skip containers without Compose labels (one diagnostic line per container) instead of
    /// reporting them under `Docker/_standalone/<name>`.
    #[serde(default = "enabled_by_default")]
    pub compose_only: bool,
    /// CPU/memory sampling period, 1..=300 s. The bars stay 5 minutes whatever this is.
    #[serde(default = "default_docker_sample_period_sec")]
    pub sample_period_sec: u64,
    /// How long `OOM killed` stays true after an OOM kill was last seen.
    #[serde(default = "default_docker_oom_latch_hours")]
    pub oom_latch_hours: u64,
    /// `project/service` patterns (`*` matches within one segment) whose services are not
    /// monitored, e.g. `portainer/*`, `lingua-ci/janitor`.
    #[serde(default)]
    pub exclude: Vec<String>,
}

impl Default for DockerConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            socket: default_docker_socket(),
            compose_only: true,
            sample_period_sec: default_docker_sample_period_sec(),
            oom_latch_hours: default_docker_oom_latch_hours(),
            exclude: Vec::new(),
        }
    }
}

impl DockerConfig {
    pub fn sample_period(&self) -> Duration {
        Duration::from_secs(self.sample_period_sec)
    }

    pub fn oom_latch(&self) -> Duration {
        Duration::from_secs(self.oom_latch_hours.saturating_mul(3600))
    }
}

fn default_docker_socket() -> PathBuf {
    PathBuf::from("/var/run/docker.sock")
}
fn default_docker_sample_period_sec() -> u64 {
    crate::probe_only::docker::contract::DEFAULT_SAMPLE_PERIOD.as_secs()
}
fn default_docker_oom_latch_hours() -> u64 {
    crate::probe_only::docker::contract::DEFAULT_OOM_LATCH_HOURS
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProbeConfig {
    #[serde(default)]
    pub host_sensors: HostSensorsConfig,
    #[serde(default)]
    pub disks: DisksConfig,
    #[serde(default)]
    pub docker: DockerConfig,
}

/// `probe.disks { enabled, exclude, writeSpeed }`: the per-filesystem disk sensors (#1481).
/// Absent = defaults (nothing excluded, write speed on; enabled unless the 0.2.x/0.3.x host
/// switches turned the disks off — see [`ProbeConfig::disks_enabled`]).
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DisksConfig {
    /// `None` = not set: the older host switches decide (an upgrade never turns disks back on).
    #[serde(default)]
    pub enabled: Option<bool>,
    /// Mount-point patterns (`*` = any run of characters) of filesystems not to report, matched
    /// against the mount point the sensors are named after (e.g. `"/mnt/usb*"`).
    #[serde(default)]
    pub exclude: Vec<String>,
    /// The `Average disk write speed on <name> disk` sensors.
    #[serde(default = "enabled_by_default")]
    pub write_speed: bool,
}

impl Default for DisksConfig {
    fn default() -> Self {
        Self {
            enabled: None,
            exclude: Vec::new(),
            write_speed: true,
        }
    }
}

impl ProbeConfig {
    /// Whether the disk sensors run. An explicit `probe.disks.enabled` decides; without it the
    /// 0.2.x/0.3.x switches that used to cover the disk sensor still do — `hostSensors.enabled:
    /// false` or `hostSensors.disk: false` — so an upgrade never switches them back on.
    pub fn disks_enabled(&self) -> bool {
        self.disks
            .enabled
            .unwrap_or(self.host_sensors.enabled && self.host_sensors.disk != Some(false))
    }
}

/// `probe.hostSensors`: the host probe-only sensors. `enabled: false` turns off both;
/// `cpuTemperature` turns off the temperature (`Logical cores` has no switch of its own — it costs
/// two records a day). The disk sensors moved to [`DisksConfig`]; the old `disk` switch is still
/// read so that an explicit `false` in a 0.2.x config keeps them off.
#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HostSensorsConfig {
    #[serde(default = "enabled_by_default")]
    pub enabled: bool,
    #[serde(default = "enabled_by_default")]
    pub cpu_temperature: bool,
    /// Deprecated (0.2.x/0.3.x): use `probe.disks.enabled`. `Some(false)` still disables the
    /// disks when `probe.disks.enabled` is not set.
    #[serde(default)]
    pub disk: Option<bool>,
}

impl Default for HostSensorsConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            cpu_temperature: true,
            disk: None,
        }
    }
}

fn enabled_by_default() -> bool {
    true
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HsmConfig {
    /// `https://host` or a bare host name (the collector defaults a bare host to HTTPS).
    pub address: String,
    pub port: u16,
    /// Path to the file holding the product access key. The key itself is never stored here.
    ///
    /// A relative path is resolved against systemd's `$CREDENTIALS_DIRECTORY` (the
    /// `LoadCredential=` drop), so the config does not hardcode the unit name; an absolute path is
    /// used as is. See `secret::resolve_key_path`.
    pub access_key_file: PathBuf,
    /// Empty by default: one product = one host, so the probe's sensors sit directly under the
    /// product (`.computer/…`, `.module/…`, `Docker/…`; owner decision, #1493). Accepted for
    /// compatibility, but a non-empty value re-introduces a `<computer>` node — not recommended.
    #[serde(default)]
    pub computer_name: String,
    /// The probe's module node, `.probe` by default (owner decision, #1496): the product root holds
    /// `.computer/…` (the host) and `.probe/` with `.module/…` and `Docker/…` — the .NET layout with
    /// an empty ComputerName. Another value renames that node — accepted, not recommended.
    #[serde(default = "default_module")]
    pub module: String,
    /// Send-queue dispatch period.
    #[serde(default = "default_package_collect_period_sec")]
    pub package_collect_period_sec: u64,
    #[serde(default = "default_request_timeout_sec")]
    pub request_timeout_sec: u64,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoggingConfig {
    /// Directory for the rolling probe log. `None` logs to stderr only (journal capture).
    #[serde(default)]
    pub directory: Option<PathBuf>,
    /// `debug`, `info`, `warn` or `error`.
    #[serde(default = "default_log_level")]
    pub level: String,
}

impl Default for LoggingConfig {
    fn default() -> Self {
        Self {
            directory: None,
            level: default_log_level(),
        }
    }
}

/// The probe's module node (#1496).
pub const DEFAULT_MODULE: &str = ".probe";

fn default_module() -> String {
    DEFAULT_MODULE.to_string()
}
fn default_package_collect_period_sec() -> u64 {
    15
}
fn default_request_timeout_sec() -> u64 {
    30
}
fn default_shutdown_timeout_sec() -> u64 {
    10
}
fn default_log_level() -> String {
    "info".to_string()
}

impl Config {
    /// Read and validate a config file.
    pub fn load(path: &Path) -> Result<Self, ConfigError> {
        let text = std::fs::read_to_string(path).map_err(|source| ConfigError::Read {
            path: path.to_path_buf(),
            source,
        })?;
        Self::parse(&text)
    }

    /// Parse and validate config text. Pure: no filesystem, no environment.
    pub fn parse(text: &str) -> Result<Self, ConfigError> {
        let config: Config = serde_json::from_str(text).map_err(ConfigError::Parse)?;
        config.validate()?;
        Ok(config)
    }

    fn validate(&self) -> Result<(), ConfigError> {
        let address = self.hsm.address.trim();
        if address.is_empty() {
            return Err(ConfigError::invalid("hsm.address must not be empty"));
        }
        // Plaintext is not configurable. The initiative (§4.3) requires peer + hostname
        // verification everywhere, including examples; a private CA is trusted by installing it in
        // the system store, not by downgrading the transport.
        // An allow-list, not a single rejected spelling: anything else (a typo'd scheme, ws://,
        // ftp://) would be passed to the collector verbatim and reach libcurl as a URL that cannot
        // be what the operator meant. A bare host stays valid - the collector defaults it to HTTPS.
        let lowercase = address.to_ascii_lowercase();
        let is_https = lowercase.starts_with("https://");
        let is_bare_host = !lowercase.contains("://");
        if !is_https && !is_bare_host {
            return Err(ConfigError::invalid(format!(
                "hsm.address must be https://<host> or a bare host name (got '{address}') - \
                 plaintext and other schemes are not configurable"
            )));
        }
        if self.hsm.port == 0 {
            return Err(ConfigError::invalid("hsm.port must be between 1 and 65535"));
        }
        if self.hsm.access_key_file.as_os_str().is_empty() {
            return Err(ConfigError::invalid(
                "hsm.accessKeyFile must name the access-key file",
            ));
        }
        for (name, value) in [
            (
                "hsm.packageCollectPeriodSec",
                self.hsm.package_collect_period_sec,
            ),
            ("hsm.requestTimeoutSec", self.hsm.request_timeout_sec),
            ("shutdownTimeoutSec", self.shutdown_timeout_sec),
        ] {
            if value == 0 {
                return Err(ConfigError::invalid(format!(
                    "{name} must be greater than zero"
                )));
            }
        }
        match self.logging.level.to_ascii_lowercase().as_str() {
            "debug" | "info" | "warn" | "error" => {}
            other => {
                return Err(ConfigError::invalid(format!(
                    "logging.level must be debug, info, warn or error (got '{other}')"
                )))
            }
        }
        self.probe.docker.validate()?;
        self.probe.disks.validate()?;
        Ok(())
    }

    pub fn shutdown_timeout(&self) -> Duration {
        Duration::from_secs(self.shutdown_timeout_sec)
    }
}

impl DisksConfig {
    fn validate(&self) -> Result<(), ConfigError> {
        if let Some(bad) = self
            .exclude
            .iter()
            .find(|pattern| pattern.trim().is_empty())
        {
            return Err(ConfigError::invalid(format!(
                "probe.disks.exclude must not contain an empty pattern (got '{bad}')"
            )));
        }
        Ok(())
    }
}

impl DockerConfig {
    fn validate(&self) -> Result<(), ConfigError> {
        if !self.enabled {
            return Ok(());
        }
        if !self.socket.is_absolute() {
            return Err(ConfigError::invalid(format!(
                "probe.docker.socket must be an absolute path (got '{}')",
                self.socket.display()
            )));
        }
        let max = crate::probe_only::docker::contract::MAX_SAMPLE_PERIOD.as_secs();
        if self.sample_period_sec == 0 || self.sample_period_sec > max {
            return Err(ConfigError::invalid(format!(
                "probe.docker.samplePeriodSec must be between 1 and {max} (got {})",
                self.sample_period_sec
            )));
        }
        if self.oom_latch_hours == 0 {
            return Err(ConfigError::invalid(
                "probe.docker.oomLatchHours must be greater than zero",
            ));
        }
        for pattern in &self.exclude {
            if !crate::probe_only::docker::identity::is_valid_pattern(pattern) {
                return Err(ConfigError::invalid(format!(
                    "probe.docker.exclude entries are 'project/service' patterns with '*' \
                     wildcards (got '{pattern}')"
                )));
            }
        }
        Ok(())
    }
}

#[derive(Debug)]
pub enum ConfigError {
    Read {
        path: PathBuf,
        source: std::io::Error,
    },
    Parse(serde_json::Error),
    Invalid(String),
}

impl ConfigError {
    fn invalid(message: impl Into<String>) -> Self {
        ConfigError::Invalid(message.into())
    }
}

impl fmt::Display for ConfigError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ConfigError::Read { path, source } => {
                write!(f, "cannot read {}: {source}", path.display())
            }
            ConfigError::Parse(source) => write!(f, "invalid config JSON: {source}"),
            ConfigError::Invalid(message) => write!(f, "invalid config: {message}"),
        }
    }
}

impl std::error::Error for ConfigError {}

#[cfg(test)]
mod tests {
    use super::*;

    const FULL: &str = r#"{
        "hsm": {
            "address": "https://garage.lan",
            "port": 44330,
            "accessKeyFile": "/run/credentials/hsm-linux-probe.service/access-key",
            "computerName": "garage-server",
            "module": "LinuxProbe",
            "packageCollectPeriodSec": 15,
            "requestTimeoutSec": 30
        },
        "logging": { "directory": "/var/log/hsm-linux-probe", "level": "info" },
        "shutdownTimeoutSec": 10
    }"#;

    const MINIMAL: &str = r#"{
        "hsm": {
            "address": "https://garage.lan",
            "port": 44330,
            "accessKeyFile": "/run/credentials/hsm-linux-probe.service/access-key"
        }
    }"#;

    #[test]
    fn full_config_maps_every_field() {
        let config = Config::parse(FULL).expect("parse");
        assert_eq!(config.hsm.address, "https://garage.lan");
        assert_eq!(config.hsm.port, 44330);
        assert_eq!(
            config.hsm.access_key_file,
            PathBuf::from("/run/credentials/hsm-linux-probe.service/access-key")
        );
        assert_eq!(config.hsm.computer_name, "garage-server");
        assert_eq!(config.hsm.module, "LinuxProbe");
        assert_eq!(
            config.logging.directory,
            Some(PathBuf::from("/var/log/hsm-linux-probe"))
        );
        assert_eq!(config.shutdown_timeout(), Duration::from_secs(10));
    }

    #[test]
    fn minimal_config_applies_documented_defaults() {
        let config = Config::parse(MINIMAL).expect("parse");
        // No computer node by default; the module node is `.probe` (#1496).
        assert_eq!(config.hsm.module, ".probe");
        assert_eq!(config.hsm.computer_name, "");
        assert_eq!(config.hsm.package_collect_period_sec, 15);
        assert_eq!(config.hsm.request_timeout_sec, 30);
        assert_eq!(config.logging.level, "info");
        assert_eq!(config.logging.directory, None);
        assert_eq!(config.shutdown_timeout_sec, 10);
    }

    #[test]
    fn the_shipped_example_config_parses() {
        // The packaged skeleton must always be a valid config; a placeholder that stopped parsing
        // would only be discovered on the operator's host.
        let example = include_str!("../../packaging/config.example.json");
        let config = Config::parse(example).expect("the packaged example must parse");
        assert!(config.hsm.address.starts_with("https://"));
        // The example spells out the Docker defaults, so it must agree with them.
        let defaults = DockerConfig::default();
        assert_eq!(config.probe.docker.enabled, defaults.enabled);
        assert_eq!(config.probe.docker.socket, defaults.socket);
        assert_eq!(config.probe.docker.compose_only, defaults.compose_only);
        assert_eq!(
            config.probe.docker.sample_period_sec,
            defaults.sample_period_sec
        );
        assert_eq!(
            config.probe.docker.oom_latch_hours,
            defaults.oom_latch_hours
        );
        assert_eq!(config.probe.docker.exclude, defaults.exclude);
        // The skeleton documents the probe-only switches, all on.
        assert!(example.contains("\"hostSensors\""));
        let host = &config.probe.host_sensors;
        assert!(host.enabled && host.cpu_temperature && host.disk.is_none());
        let disks = &config.probe.disks;
        assert!(example.contains("\"disks\""));
        assert_eq!(disks.enabled, Some(true));
        assert!(disks.write_speed && disks.exclude.is_empty());
        assert!(config.probe.disks_enabled());
    }

    #[test]
    fn a_config_from_the_earlier_probe_still_loads() {
        // The `sampling` section configured probe-only sensors removed in the parity phase. A
        // deployed config that still carries it must keep loading, not stop the service.
        let old = r#"{
            "hsm": { "address": "https://garage.lan", "port": 44330, "accessKeyFile": "access-key" },
            "sampling": { "loadAveragePeriodSec": 60, "logicalCoresPeriodSec": 86400 },
            "shutdownTimeoutSec": 10
        }"#;
        let config = Config::parse(old).expect("an old config must still parse");
        assert_eq!(config.hsm.port, 44330);
    }

    #[test]
    fn host_sensors_default_to_on_when_the_section_is_absent() {
        // A config written before the probe-only sensors existed (the deployed trial's) must turn
        // them on, not fail and not silently leave them off.
        let config = Config::parse(MINIMAL).expect("parse");
        let host = &config.probe.host_sensors;
        assert!(host.enabled && host.cpu_temperature && host.disk.is_none());
        let disks = &config.probe.disks;
        assert!(disks.enabled.is_none() && disks.write_speed && disks.exclude.is_empty());
        assert!(config.probe.disks_enabled());
    }

    #[test]
    fn the_disks_section_maps_and_the_old_disk_switch_is_still_read() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "disks": { "exclude": ["/mnt/usb*", "/media/*"], "writeSpeed": false } } }"#;
        let probe = Config::parse(text).expect("parse").probe;
        assert!(probe.disks_enabled());
        assert!(!probe.disks.write_speed);
        assert_eq!(probe.disks.exclude, vec!["/mnt/usb*", "/media/*"]);

        // A 0.3.x config with all host sensors off had the disk sensor off too: it stays off
        // unless probe.disks.enabled says otherwise.
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "enabled": false } } }"#;
        assert!(!Config::parse(text).expect("parse").probe.disks_enabled());
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "enabled": false }, "disks": { "enabled": true } } }"#;
        assert!(Config::parse(text).expect("parse").probe.disks_enabled());

        // A 0.2.x config that switched the disk sensors off keeps them off (probe_only reads it).
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "disk": false } } }"#;
        let config = Config::parse(text).expect("parse");
        assert_eq!(config.probe.host_sensors.disk, Some(false));
        assert!(!config.probe.disks_enabled());

        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "disks": { "exclude": ["  "] } } }"#;
        assert!(matches!(Config::parse(text), Err(ConfigError::Invalid(_))));
    }

    #[test]
    fn each_host_source_can_be_turned_off_and_omitted_switches_stay_on() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "cpuTemperature": false } } }"#;
        let host = Config::parse(text).expect("parse").probe.host_sensors;
        assert!(host.enabled);
        assert!(!host.cpu_temperature);
        assert!(host.disk.is_none());

        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "enabled": false } } }"#;
        let host = Config::parse(text).expect("parse").probe.host_sensors;
        assert!(!host.enabled);
    }

    #[test]
    fn a_non_boolean_switch_is_rejected() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "hostSensors": { "disk": "no" } } }"#;
        assert!(matches!(Config::parse(text), Err(ConfigError::Parse(_))));
    }

    #[test]
    fn config_carries_no_inline_secret_field() {
        // Guards the §4.3 contract: the key is referenced by path, never embedded.
        let with_key = r#"{ "hsm": { "address": "https://garage.lan", "port": 44330,
            "accessKeyFile": "/run/credentials/key", "accessKey": "leaked-secret" } }"#;
        let config = Config::parse(with_key).expect("unknown fields are ignored");
        // The key field is not part of the schema, so it is dropped rather than used.
        assert!(!format!("{config:?}").contains("leaked-secret"));
    }

    #[test]
    fn missing_required_fields_are_rejected() {
        for text in [
            r#"{}"#,
            r#"{ "hsm": {} }"#,
            r#"{ "hsm": { "address": "https://garage.lan" } }"#,
            r#"{ "hsm": { "address": "https://garage.lan", "port": 44330 } }"#,
            r#"{ "hsm": { "port": 44330, "accessKeyFile": "/k" } }"#,
        ] {
            assert!(
                matches!(Config::parse(text), Err(ConfigError::Parse(_))),
                "expected a parse error for {text}"
            );
        }
    }

    #[test]
    fn a_bare_host_is_accepted_and_left_to_the_collector() {
        // The collector defaults a scheme-less address to HTTPS, so this is not a plaintext hole.
        let text =
            r#"{ "hsm": { "address": "garage.lan", "port": 44330, "accessKeyFile": "/k" } }"#;
        assert_eq!(
            Config::parse(text).expect("parse").hsm.address,
            "garage.lan"
        );
    }

    #[test]
    fn every_non_https_scheme_is_rejected_whatever_its_spelling() {
        // The contract is "must be https", so the check is an allow-list: a rejected-prefix test
        // would pass while ftp:// or a typo sailed through to libcurl.
        for address in [
            "http://garage.lan",
            "HTTP://garage.lan",
            "Http://garage.lan",
            "ftp://garage.lan",
            "ws://garage.lan",
            "htps://garage.lan",
        ] {
            let text = format!(
                r#"{{ "hsm": {{ "address": "{address}", "port": 44330, "accessKeyFile": "/k" }} }}"#
            );
            let error = Config::parse(&text).expect_err("must be rejected");
            assert!(
                matches!(error, ConfigError::Invalid(_)),
                "{address}: {error}"
            );
        }
        // Case does not matter for the accepted spelling either.
        let text =
            r#"{ "hsm": { "address": "HTTPS://garage.lan", "port": 1, "accessKeyFile": "/k" } }"#;
        assert!(Config::parse(text).is_ok());
    }

    #[test]
    fn plaintext_address_is_rejected() {
        let text = r#"{ "hsm": { "address": "http://garage.lan", "port": 44330, "accessKeyFile": "/k" } }"#;
        let error = Config::parse(text).expect_err("plaintext must be rejected");
        assert!(matches!(error, ConfigError::Invalid(_)), "{error}");
        assert!(error.to_string().contains("https://"));
    }

    #[test]
    fn zero_valued_periods_and_ports_are_rejected() {
        for text in [
            r#"{ "hsm": { "address": "https://g", "port": 0, "accessKeyFile": "/k" } }"#,
            r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k",
                 "packageCollectPeriodSec": 0 } }"#,
            r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
                 "shutdownTimeoutSec": 0 }"#,
        ] {
            assert!(
                matches!(Config::parse(text), Err(ConfigError::Invalid(_))),
                "expected a validation error for {text}"
            );
        }
    }

    #[test]
    fn blank_address_and_empty_key_path_are_rejected() {
        let blank = r#"{ "hsm": { "address": "   ", "port": 44330, "accessKeyFile": "/k" } }"#;
        assert!(matches!(Config::parse(blank), Err(ConfigError::Invalid(_))));
        let no_key = r#"{ "hsm": { "address": "https://g", "port": 44330, "accessKeyFile": "" } }"#;
        assert!(matches!(
            Config::parse(no_key),
            Err(ConfigError::Invalid(_))
        ));
    }

    #[test]
    fn an_unknown_log_level_is_rejected() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "logging": { "level": "verbose" } }"#;
        assert!(matches!(Config::parse(text), Err(ConfigError::Invalid(_))));
    }

    #[test]
    fn malformed_json_is_rejected() {
        assert!(matches!(
            Config::parse("{ not json"),
            Err(ConfigError::Parse(_))
        ));
        assert!(matches!(Config::parse(""), Err(ConfigError::Parse(_))));
    }

    #[test]
    fn the_docker_source_defaults_to_on_compose_only_every_five_seconds() {
        let config = Config::parse(MINIMAL).expect("parse");
        assert!(config.probe.docker.enabled);
        assert_eq!(
            config.probe.docker.socket,
            PathBuf::from("/var/run/docker.sock")
        );
        assert!(config.probe.docker.compose_only);
        assert_eq!(config.probe.docker.sample_period(), Duration::from_secs(5));
        assert_eq!(
            config.probe.docker.oom_latch(),
            Duration::from_secs(24 * 3600)
        );
    }

    #[test]
    fn the_docker_section_maps_every_field() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "docker": { "enabled": false, "socket": "/run/docker.sock",
                         "composeOnly": false, "samplePeriodSec": 10, "oomLatchHours": 48 } } }"#;
        let docker = Config::parse(text).expect("parse").probe.docker;
        assert!(!docker.enabled);
        assert_eq!(docker.socket, PathBuf::from("/run/docker.sock"));
        assert!(!docker.compose_only);
        assert_eq!(docker.sample_period_sec, 10);
        assert_eq!(docker.oom_latch_hours, 48);
    }

    #[test]
    fn docker_exclusions_parse_and_are_validated() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "docker": { "exclude": ["portainer/*", "lingua-ci/janitor"] } } }"#;
        let docker = Config::parse(text).expect("parse").probe.docker;
        assert_eq!(docker.exclude, vec!["portainer/*", "lingua-ci/janitor"]);
        assert!(DockerConfig::default().exclude.is_empty());
        for bad in ["portainer", "a/b/c", "/db", "gitea/", ""] {
            let text = format!(
                r#"{{ "hsm": {{ "address": "https://g", "port": 1, "accessKeyFile": "/k" }},
                     "probe": {{ "docker": {{ "exclude": ["{bad}"] }} }} }}"#
            );
            assert!(
                matches!(Config::parse(&text), Err(ConfigError::Invalid(_))),
                "{bad}"
            );
        }
    }

    #[test]
    fn invalid_docker_settings_are_rejected() {
        for docker in [
            r#"{ "socket": "docker.sock" }"#,
            r#"{ "samplePeriodSec": 0 }"#,
            r#"{ "samplePeriodSec": 301 }"#,
            r#"{ "oomLatchHours": 0 }"#,
        ] {
            let text = format!(
                r#"{{ "hsm": {{ "address": "https://g", "port": 1, "accessKeyFile": "/k" }},
                     "probe": {{ "docker": {docker} }} }}"#
            );
            assert!(
                matches!(Config::parse(&text), Err(ConfigError::Invalid(_))),
                "{docker}"
            );
        }
        // A disabled source is not validated: nothing reads its settings.
        let off = r#"{ "hsm": { "address": "https://g", "port": 1, "accessKeyFile": "/k" },
             "probe": { "docker": { "enabled": false, "samplePeriodSec": 0 } } }"#;
        assert!(Config::parse(off).is_ok());
    }

    #[test]
    fn a_port_above_the_u16_range_is_rejected() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 70000, "accessKeyFile": "/k" } }"#;
        assert!(matches!(Config::parse(text), Err(ConfigError::Parse(_))));
    }
}
