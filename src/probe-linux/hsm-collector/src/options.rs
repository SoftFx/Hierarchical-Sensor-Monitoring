use std::fmt;
use std::time::Duration;

use hsm_collector_sys as sys;

/// The managed collector's `CollectorOptions.ExceptionDeduplicatorWindow` default.
pub const DEFAULT_EXCEPTION_DEDUPLICATOR_WINDOW: Duration = Duration::from_secs(60 * 60);

/// Connection and pipeline options for a [`crate::Collector`].
///
/// Deliberately **does not** expose `allow_untrusted_server_certificate`. That ABI flag turns off
/// both peer and hostname verification, which the Linux-probe initiative (§4.1/§4.3) bans outright:
/// a private CA is trusted by installing it in the system trust store, which libcurl/OpenSSL picks
/// up with verification left on.
#[derive(Clone)]
pub struct CollectorOptions {
    /// Product access key. Treated as a secret: never logged, never put in argv, and redacted from
    /// this type's `Debug` output.
    pub access_key: String,
    /// `https://host` (or `http://host` together with [`Self::allow_plaintext_transport`]).
    pub server_address: String,
    pub port: u16,
    pub client_name: Option<String>,
    /// Path prefix for the module node.
    pub module: Option<String>,
    /// Path prefix for the computer node.
    pub computer_name: Option<String>,
    /// Dispatch period of the send queue; `None` keeps the collector default (15 s).
    pub package_collect_period: Option<Duration>,
    pub request_timeout: Option<Duration>,
    pub max_queue_size: Option<u32>,
    pub max_values_in_package: Option<u32>,
    /// Allow a plaintext `http://` endpoint. Test-only: production configuration cannot set it.
    pub allow_plaintext_transport: bool,
    /// How long an identical error message is suppressed after its first occurrence.
    ///
    /// Always sent explicitly: the collector reads 0 as "log every occurrence", not as "take the
    /// default" (see [`crate::Collector::new`]). Defaults to the managed collector's 1 hour.
    pub exception_deduplicator_window: Duration,
}

impl CollectorOptions {
    pub fn new(
        access_key: impl Into<String>,
        server_address: impl Into<String>,
        port: u16,
    ) -> Self {
        Self {
            access_key: access_key.into(),
            server_address: server_address.into(),
            port,
            client_name: None,
            module: None,
            computer_name: None,
            package_collect_period: None,
            request_timeout: None,
            max_queue_size: None,
            max_values_in_package: None,
            allow_plaintext_transport: false,
            exception_deduplicator_window: DEFAULT_EXCEPTION_DEDUPLICATOR_WINDOW,
        }
    }
}

impl fmt::Debug for CollectorOptions {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("CollectorOptions")
            .field("access_key", &"<redacted>")
            .field("server_address", &self.server_address)
            .field("port", &self.port)
            .field("client_name", &self.client_name)
            .field("module", &self.module)
            .field("computer_name", &self.computer_name)
            .field("package_collect_period", &self.package_collect_period)
            .field("request_timeout", &self.request_timeout)
            .field("max_queue_size", &self.max_queue_size)
            .field("max_values_in_package", &self.max_values_in_package)
            .field("allow_plaintext_transport", &self.allow_plaintext_transport)
            .field(
                "exception_deduplicator_window",
                &self.exception_deduplicator_window,
            )
            .finish()
    }
}

/// Registration metadata for a sensor. `None` everywhere reproduces the plain create-sensor
/// registration byte for byte.
#[derive(Clone, Debug, Default)]
pub struct SensorOptions {
    pub ttl: Option<Duration>,
    pub description: Option<String>,
    /// Code from the managed `Unit` enum; `None` emits null.
    pub unit: Option<i32>,
    pub keep_history: Option<Duration>,
    /// `AggregateData`: the server stores a value only when it differs from the previous one (the
    /// managed state sensors, e.g. `Service status`). `None` emits null.
    pub aggregate_data: Option<bool>,
    /// `EnableGrafana`; `None` emits null.
    pub enable_grafana: Option<bool>,
    /// Anchor the path at the computer node (`<computer>/<path>`) instead of
    /// `<computer>/<module>/<path>`; host sensors pass a `.computer/…` path. Also forces
    /// `IsSingletonSensor` on the wire, as the managed `IsComputerSensor` does.
    pub is_computer_sensor: bool,
    /// `Statistics` flags (managed `StatisticsOptions`; [`STATISTICS_EMA`] = 1). `None` emits the
    /// kind's default. EMA must be on for an `Ema*` alert property to have values.
    pub statistics: Option<i32>,
}

/// `StatisticsOptions.EMA`: the server keeps an exponential moving average of the sensor.
pub const STATISTICS_EMA: i32 = 1;

impl SensorOptions {
    pub fn with_statistics(mut self, statistics: i32) -> Self {
        self.statistics = Some(statistics);
        self
    }

    pub fn with_keep_history(mut self, keep_history: Duration) -> Self {
        self.keep_history = Some(keep_history);
        self
    }

    pub fn with_aggregate_data(mut self, aggregate_data: bool) -> Self {
        self.aggregate_data = Some(aggregate_data);
        self
    }

    pub fn with_enable_grafana(mut self, enable_grafana: bool) -> Self {
        self.enable_grafana = Some(enable_grafana);
        self
    }

    pub fn with_is_computer_sensor(mut self, is_computer_sensor: bool) -> Self {
        self.is_computer_sensor = is_computer_sensor;
        self
    }

    pub fn with_ttl(mut self, ttl: Duration) -> Self {
        self.ttl = Some(ttl);
        self
    }

    pub fn with_description(mut self, description: impl Into<String>) -> Self {
        self.description = Some(description.into());
        self
    }

    pub fn with_unit(mut self, unit: i32) -> Self {
        self.unit = Some(unit);
        self
    }

    /// Fills a C options struct that was obtained from `hsm_sensor_options_default()`.
    ///
    /// `description` must be a pointer that stays alive for the duration of the ABI call.
    pub(crate) fn apply(&self, raw: &mut sys::hsm_sensor_options_t) {
        if let Some(ttl) = self.ttl {
            raw.ttl_ms = clamp_millis(ttl);
        }
        if let Some(keep_history) = self.keep_history {
            raw.keep_history_ms = clamp_millis(keep_history);
        }
        if let Some(unit) = self.unit {
            raw.unit = unit;
        }
        if let Some(aggregate_data) = self.aggregate_data {
            raw.aggregate_data = tri_state(aggregate_data);
        }
        if let Some(enable_grafana) = self.enable_grafana {
            raw.enable_grafana = tri_state(enable_grafana);
        }
        raw.is_computer_sensor = self.is_computer_sensor;
        if let Some(statistics) = self.statistics {
            raw.statistics = statistics;
        }
    }
}

/// The ABI's tri-state bool: -1 = null (left at the default sentinel), 0 = false, 1 = true.
fn tri_state(value: bool) -> i32 {
    i32::from(value)
}

/// One registered option of an enum sensor.
#[derive(Clone, Debug)]
pub struct EnumOption {
    pub key: i32,
    pub value: String,
    /// ARGB color; 0 lets the server pick.
    pub color: i32,
    pub description: Option<String>,
}

impl EnumOption {
    pub fn new(key: i32, value: impl Into<String>) -> Self {
        Self {
            key,
            value: value.into(),
            color: 0,
            description: None,
        }
    }
}

/// Status attached to a posted sensor value.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum SensorStatus {
    OffTime,
    Ok,
    Warning,
    Error,
}

impl SensorStatus {
    pub(crate) fn as_raw(self) -> sys::hsm_sensor_status_t {
        match self {
            SensorStatus::OffTime => sys::HSM_SENSOR_STATUS_OFF_TIME,
            SensorStatus::Ok => sys::HSM_SENSOR_STATUS_OK,
            SensorStatus::Warning => sys::HSM_SENSOR_STATUS_WARNING,
            SensorStatus::Error => sys::HSM_SENSOR_STATUS_ERROR,
        }
    }
}

/// Level of a message emitted by the collector's log sink.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub enum LogLevel {
    Debug,
    Info,
    Error,
}

impl LogLevel {
    pub(crate) fn from_raw(level: sys::hsm_log_level_t) -> Self {
        match level {
            sys::HSM_LOG_LEVEL_DEBUG => LogLevel::Debug,
            sys::HSM_LOG_LEVEL_ERROR => LogLevel::Error,
            // An unknown level is treated as Info rather than dropped: losing a diagnostic is
            // worse than mislabeling it.
            _ => LogLevel::Info,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            LogLevel::Debug => "DEBUG",
            LogLevel::Info => "INFO",
            LogLevel::Error => "ERROR",
        }
    }
}

/// Lifecycle state of a collector.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum CollectorStatus {
    Stopped,
    Starting,
    Running,
    Stopping,
    Disposed,
    /// A code this wrapper does not know; the ABI gained a state.
    Unknown(i32),
}

impl CollectorStatus {
    pub(crate) fn from_raw(status: sys::hsm_collector_status_t) -> Self {
        match status {
            sys::HSM_COLLECTOR_STATUS_STOPPED => CollectorStatus::Stopped,
            sys::HSM_COLLECTOR_STATUS_STARTING => CollectorStatus::Starting,
            sys::HSM_COLLECTOR_STATUS_RUNNING => CollectorStatus::Running,
            sys::HSM_COLLECTOR_STATUS_STOPPING => CollectorStatus::Stopping,
            sys::HSM_COLLECTOR_STATUS_DISPOSED => CollectorStatus::Disposed,
            other => CollectorStatus::Unknown(other),
        }
    }
}

/// Saturating millisecond conversion: the ABI takes `int64_t` ms, and a `Duration` can exceed it.
pub(crate) fn clamp_millis(duration: Duration) -> i64 {
    i64::try_from(duration.as_millis()).unwrap_or(i64::MAX)
}

/// Saturating millisecond conversion into the `int32_t` pipeline fields.
pub(crate) fn clamp_millis_i32(duration: Duration) -> i32 {
    i32::try_from(duration.as_millis()).unwrap_or(i32::MAX)
}
