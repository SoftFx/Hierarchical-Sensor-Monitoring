//! The Docker source's sensor contract in one place (owner decisions, #1416).
//!
//! Every number and name the contract fixes lives here — cadences, bar shape, thresholds,
//! confirmation periods, retention — so a change to the agreed contract is a change to this file
//! and nowhere else. The README "Probe-only sensors" table and initiative §4.2a document the same
//! values; `tests::the_contract_matches_the_agreed_table` pins them.

use std::time::Duration;

/// Root node of the Docker tree, under the probe's module: `<computer>/<module>/Docker/…`.
pub const ROOT: &str = "Docker";

/// Pseudo-project for containers without Compose labels when `docker.composeOnly` is `false`.
/// Compose project names must start with a letter or digit, so this can never collide with one.
pub const STANDALONE_PROJECT: &str = "_standalone";

// ---- Sensor names (the last path segment) ------------------------------------------------------

pub const CPU: &str = "CPU";
pub const MEMORY_USED: &str = "Memory used %";
pub const SERVICE_STATUS: &str = "Service status";
pub const HEALTH: &str = "Health";
pub const RESTART_COUNT: &str = "Restart count";
pub const OOM_KILLED: &str = "OOM killed";
pub const DISK_WRITTEN: &str = "Disk written per hour";

// ---- Cadences ----------------------------------------------------------------------------------

/// Default stats sampling period (`docker.samplePeriodSec`).
pub const DEFAULT_SAMPLE_PERIOD: Duration = Duration::from_secs(5);
/// Upper bound on `docker.samplePeriodSec`: a sample period longer than the bar leaves bars empty.
pub const MAX_SAMPLE_PERIOD: Duration = BAR_PERIOD;
/// The state poll: list + inspect, feeding status, health, restart count and OOM.
pub const STATE_POLL_PERIOD: Duration = Duration::from_secs(60);
/// CPU and memory bars: one bar per 5 minutes (288 records/day per sensor).
pub const BAR_PERIOD: Duration = Duration::from_secs(5 * 60);
/// Handed to the collector as the bar's post period. Public native bars roll on add and do not
/// schedule partial posts (`hsm_collector.h`), so this only matters if that changes; it is the
/// catalog's `PostDataPeriod`.
pub const BAR_POST_PERIOD: Duration = Duration::from_secs(15);
/// Digits kept in bar values.
pub const BAR_PRECISION: i32 = 2;

/// A CPU delta is used only when the two samples are between these multiples of the sample period
/// apart; otherwise it is skipped (a gap after an outage must not become one averaged sample).
pub const MIN_INTERVAL_FACTOR: f64 = 0.5;
pub const MAX_INTERVAL_FACTOR: f64 = 3.0;

// ---- Retention ---------------------------------------------------------------------------------

/// A (project, service) seen before but now absent reports `Stopped` for this long after it was
/// last seen, then is forgotten.
pub const VANISHED_SERVICE_RETENTION: Duration = Duration::from_secs(7 * 24 * 3600);
/// Default `docker.oomLatchHours`: an OOM kill keeps `OOM killed = true` for a day.
pub const DEFAULT_OOM_LATCH_HOURS: u64 = 24;

// ---- Alert thresholds (attached at registration, see `alerts.rs`) ------------------------------

/// CPU: bar mean above this → Warning + notification.
pub const CPU_ALERT_MEAN_PERCENT: f64 = 90.0;
/// CPU: the condition must hold this long.
pub const CPU_ALERT_CONFIRMATION: Duration = Duration::from_secs(30 * 60);
/// Memory used %: bar mean above this → Warning + notification.
pub const MEMORY_ALERT_MEAN_PERCENT: f64 = 90.0;
/// Service status ≠ Running for this long → notification (the Windows service-status prototype).
pub const SERVICE_STATUS_ALERT_CONFIRMATION: Duration = Duration::from_secs(5 * 60);
/// Health = unhealthy for this long → notification.
pub const HEALTH_ALERT_CONFIRMATION: Duration = Duration::from_secs(5 * 60);

// ---- Units (managed `Unit` enum codes) ---------------------------------------------------------

pub const UNIT_MB: i32 = 3;
pub const UNIT_PERCENTS: i32 = 100;
pub const UNIT_COUNT: i32 = 1100;

/// Bytes per reported megabyte. The collector's own memory sensors report MiB as "MB".
pub const BYTES_PER_MB: u64 = 1024 * 1024;
/// Bytes per megabyte of `Disk written per hour`: decimal, the unit SSD endurance (TBW) is rated
/// in, so 1000 of them are the decimal GB the disks' `Written today` reports.
pub const BYTES_PER_DECIMAL_MB: f64 = 1_000_000.0;

/// How often a changed `Disk written per hour` accumulator is written to the state file at most
/// (it is also written when an hour is posted and when the probe stops). A crash inside the hour
/// loses nothing: the persisted baselines pair with the next counters, which cover the lost minutes.
pub const WRITTEN_PERSIST_PERIOD: Duration = Duration::from_secs(5 * 60);

// ---- Engine API --------------------------------------------------------------------------------

/// The Engine API version the probe speaks, pinned in every request path. The fixtures are from a
/// 1.45 daemon (Docker 26.1, Debian 13). See `engine::negotiate_api_version` for older/newer daemons.
pub const PINNED_API_VERSION: (u32, u32) = (1, 45);
/// Oldest version the probe accepts: `one-shot=true` stats appeared in 1.41; before it a
/// non-streaming stats call blocks for a second collecting a second sample.
pub const OLDEST_API_VERSION: (u32, u32) = (1, 41);
/// Per-request bound on connect + write + read. A hung daemon costs at most this per call; on
/// garage a one-shot stats call takes ~2 ms (p95 2.5 ms). Kept under the probe's 2 s stop wait for
/// its source threads, so a stop never finds this thread stuck in a call.
pub const REQUEST_TIMEOUT: Duration = Duration::from_millis(1500);
/// Largest response body accepted. The garage list is ~30 KB, an inspect ~8 KB; the cap keeps a
/// misbehaving peer from growing the probe towards its `MemoryMax=64M`.
pub const MAX_RESPONSE_BYTES: usize = 4 * 1024 * 1024;
/// Backoff after the Engine API fails: starts at the sample period, doubles, capped here.
pub const MAX_BACKOFF: Duration = STATE_POLL_PERIOD;

// ---- Enum options ------------------------------------------------------------------------------

/// `Service status` keys — the managed `ServiceControllerStatus` values, exactly as the Windows
/// `ServiceStatusPrototype` (and the native collector's service-status sensor) registers them.
pub mod service_status {
    pub const STOPPED: i32 = 1;
    pub const START_PENDING: i32 = 2;
    pub const STOP_PENDING: i32 = 3;
    pub const RUNNING: i32 = 4;
    pub const CONTINUE_PENDING: i32 = 5;
    pub const PAUSE_PENDING: i32 = 6;
    pub const PAUSED: i32 = 7;

    /// `(key, name, description, color)` byte-for-byte from
    /// `src/collector/HSMDataCollector/Prototypes/Collections/ModuleInfoCollections.cs`
    /// (`ServiceStatusPrototype`) — the same option set every Windows host registers, so one HSM
    /// view and one alert template read both.
    pub const OPTIONS: [(i32, &str, &str, i32); 7] = [
        (STOPPED, "Stopped", "The service is stopped.", 0xFF0000),
        (
            START_PENDING,
            "StartPending",
            "The service start pending.",
            0xBFFFBF,
        ),
        (
            STOP_PENDING,
            "StopPending",
            "The service stop pending.",
            0xFD6464,
        ),
        (RUNNING, "Running", "The service is running.", 0x00FF00),
        (
            CONTINUE_PENDING,
            "ContinuePending",
            "The service continue is pending",
            0xFFB403,
        ),
        (
            PAUSE_PENDING,
            "PausePending",
            "The service pause is pending.",
            0x809EFF,
        ),
        (PAUSED, "Paused", "The service is paused.", 0x0314FF),
    ];
}

/// `Health` keys: Docker's `State.Health.Status` values that carry information.
pub mod health {
    pub const STARTING: i32 = 1;
    pub const HEALTHY: i32 = 2;
    pub const UNHEALTHY: i32 = 3;

    pub const OPTIONS: [(i32, &str, &str, i32); 3] = [
        (
            STARTING,
            "starting",
            "The container's healthcheck has not passed yet (start period).",
            0xFFB403,
        ),
        (
            HEALTHY,
            "healthy",
            "The container's healthcheck passes.",
            0x00FF00,
        ),
        (
            UNHEALTHY,
            "unhealthy",
            "The container's healthcheck fails.",
            0xFF0000,
        ),
    ];
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_contract_matches_the_agreed_table() {
        // SENSORS-DECISIONS.md §2 as agreed with the owner. Changing any of these is a contract
        // change: update the README table and initiative §4.2a with it.
        assert_eq!(DEFAULT_SAMPLE_PERIOD, Duration::from_secs(5));
        assert_eq!(BAR_PERIOD, Duration::from_secs(300));
        assert_eq!(STATE_POLL_PERIOD, Duration::from_secs(60));
        assert_eq!(CPU_ALERT_MEAN_PERCENT, 90.0);
        assert_eq!(CPU_ALERT_CONFIRMATION, Duration::from_secs(1800));
        assert_eq!(MEMORY_ALERT_MEAN_PERCENT, 90.0);
        assert_eq!(SERVICE_STATUS_ALERT_CONFIRMATION, Duration::from_secs(300));
        assert_eq!(HEALTH_ALERT_CONFIRMATION, Duration::from_secs(300));
        assert_eq!(VANISHED_SERVICE_RETENTION, Duration::from_secs(7 * 86_400));
        assert_eq!(DEFAULT_OOM_LATCH_HOURS, 24);
        // Disk written per hour (owner decision): decimal MB, one value per clock hour.
        assert_eq!(DISK_WRITTEN, "Disk written per hour");
        assert_eq!(UNIT_MB, 3);
        assert_eq!(BYTES_PER_DECIMAL_MB, 1e6);
    }

    #[test]
    fn service_status_options_are_the_windows_prototype_ones() {
        // The managed ServiceControllerStatus values, names and ARGB colors; the native collector
        // registers the identical set for its Windows service-status sensor.
        let keys: Vec<i32> = service_status::OPTIONS.iter().map(|o| o.0).collect();
        assert_eq!(keys, vec![1, 2, 3, 4, 5, 6, 7]);
        assert_eq!(service_status::OPTIONS[3].1, "Running");
        assert_eq!(service_status::OPTIONS[0].3, 0xFF0000);
        assert_eq!(service_status::OPTIONS[1].3, 12_582_847); // 0xBFFFBF, as the native golden
    }
}
