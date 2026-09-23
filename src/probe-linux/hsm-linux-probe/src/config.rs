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

/// Default location of the config file (a dpkg conffile, so operator edits survive upgrades).
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
    #[serde(default)]
    pub computer_name: String,
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

fn default_module() -> String {
    "LinuxProbe".to_string()
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
        Ok(())
    }

    pub fn shutdown_timeout(&self) -> Duration {
        Duration::from_secs(self.shutdown_timeout_sec)
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
        assert_eq!(config.hsm.module, "LinuxProbe");
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
    fn a_port_above_the_u16_range_is_rejected() {
        let text = r#"{ "hsm": { "address": "https://g", "port": 70000, "accessKeyFile": "/k" } }"#;
        assert!(matches!(Config::parse(text), Err(ConfigError::Parse(_))));
    }
}
