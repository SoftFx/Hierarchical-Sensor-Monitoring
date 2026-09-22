//! Probe logging: stderr (captured by the journal under systemd) plus an optional file.
//!
//! The collector's own diagnostics are routed into the same sink, so one file holds the whole
//! story. The line format matches the collector's built-in file logger —
//! `yyyy-MM-dd HH:mm:ss|LEVEL| message` — so the two are greppable together.

use std::fs::{File, OpenOptions};
use std::io::Write;
use std::path::Path;
use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};

use hsm_collector::LogLevel;

/// Probe log level. A superset of the collector's three levels: the probe also needs `Warn` for
/// conditions that are not failures but must not hide at `info` (e.g. values dropped at stop).
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub enum Level {
    Debug,
    Info,
    Warn,
    Error,
}

impl Level {
    pub fn as_str(self) -> &'static str {
        match self {
            Level::Debug => "DEBUG",
            Level::Info => "INFO",
            Level::Warn => "WARN",
            Level::Error => "ERROR",
        }
    }
}

impl From<LogLevel> for Level {
    fn from(level: LogLevel) -> Self {
        match level {
            LogLevel::Debug => Level::Debug,
            LogLevel::Info => Level::Info,
            LogLevel::Error => Level::Error,
        }
    }
}

/// The level at which a collector message is written.
///
/// Normally the collector's own level. One exception, for root CLAUDE.md rule #8 (no silent data
/// loss): the native collector reports values discarded by its bounded stop drain at `debug`
/// ("Collector stop dropped N pending value(s): …"), which a production `info` log never shows.
/// A non-zero count is data loss and is raised to `Warn`.
pub fn collector_message_level(level: LogLevel, message: &str) -> Level {
    const DROPPED_AT_STOP: &str = "Collector stop dropped ";
    if let Some(rest) = message.strip_prefix(DROPPED_AT_STOP) {
        let count: String = rest.chars().take_while(char::is_ascii_digit).collect();
        if count.parse::<u64>().is_ok_and(|dropped| dropped > 0) {
            return Level::Warn.max(level.into());
        }
    }
    level.into()
}

pub struct Logger {
    min_level: Level,
    file: Option<Mutex<LogFile>>,
}

/// The open log file together with the UTC date it was opened for, so the daemon can roll instead
/// of growing one file for as long as it runs.
struct LogFile {
    directory: std::path::PathBuf,
    date: String,
    handle: File,
}

impl Logger {
    /// Create a logger. A file sink is added when `directory` is given and can be opened; a
    /// failure there is reported to stderr and downgraded to stderr-only logging, because losing
    /// the log file must not stop the probe from monitoring.
    pub fn new(min_level: Level, directory: Option<&Path>) -> Self {
        let file = directory.and_then(|dir| {
            let date = utc_date(now_unix_seconds());
            match open_log_file(dir, &date) {
                Ok(handle) => Some(Mutex::new(LogFile {
                    directory: dir.to_path_buf(),
                    date,
                    handle,
                })),
                Err(error) => {
                    eprintln!(
                        "{}| cannot open the log file in {}: {error}",
                        prefix(Level::Error),
                        dir.display()
                    );
                    None
                }
            }
        });
        Self { min_level, file }
    }

    pub fn log(&self, level: Level, message: &str) {
        if level < self.min_level {
            return;
        }
        let now = now_unix_seconds();
        let line = format!("{}|{}| {message}", utc_timestamp(now), level.as_str());
        eprintln!("{line}");

        if let Some(file) = &self.file {
            let mut file = file.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
            file.roll_if_needed(&utc_date(now));
            // A write failure is not worth crashing a monitoring daemon over, and re-logging it
            // here would recurse.
            let _ = writeln!(file.handle, "{line}");
            let _ = file.handle.flush();
        }
    }

    pub fn debug(&self, message: impl AsRef<str>) {
        self.log(Level::Debug, message.as_ref());
    }

    pub fn info(&self, message: impl AsRef<str>) {
        self.log(Level::Info, message.as_ref());
    }

    pub fn warn(&self, message: impl AsRef<str>) {
        self.log(Level::Warn, message.as_ref());
    }

    pub fn error(&self, message: impl AsRef<str>) {
        self.log(Level::Error, message.as_ref());
    }
}

/// Parse a configured level name. Validation happens in the config layer; anything unexpected here
/// falls back to `Info` rather than silencing the probe.
pub fn parse_level(name: &str) -> Level {
    match name.to_ascii_lowercase().as_str() {
        "debug" => Level::Debug,
        "warn" => Level::Warn,
        "error" => Level::Error,
        _ => Level::Info,
    }
}

impl LogFile {
    /// Reopen for a new UTC date. A failed reopen keeps the previous handle: still writing to
    /// yesterday's file beats losing the log entirely.
    fn roll_if_needed(&mut self, today: &str) {
        if self.date == today {
            return;
        }
        if let Ok(handle) = open_log_file(&self.directory, today) {
            self.handle = handle;
            self.date = today.to_string();
        }
    }
}

/// File name for a given UTC date. Mirrors the collector's own rolling file logger, which names
/// its files by UTC date so the two sit side by side in the same directory listing.
fn log_file_name(date: &str) -> String {
    format!("hsm-linux-probe_{date}.log")
}

fn open_log_file(directory: &Path, date: &str) -> std::io::Result<File> {
    std::fs::create_dir_all(directory)?;
    OpenOptions::new()
        .create(true)
        .append(true)
        .open(directory.join(log_file_name(date)))
}

fn prefix(level: Level) -> String {
    format!("{}|{}", utc_timestamp(now_unix_seconds()), level.as_str())
}

fn now_unix_seconds() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| elapsed.as_secs() as i64)
        .unwrap_or(0)
}

/// `yyyy-MM-dd HH:mm:ss` in UTC.
fn utc_timestamp(unix_seconds: i64) -> String {
    let (year, month, day) = civil_from_unix_days(unix_seconds.div_euclid(86_400));
    let seconds_of_day = unix_seconds.rem_euclid(86_400);
    format!(
        "{year:04}-{month:02}-{day:02} {:02}:{:02}:{:02}",
        seconds_of_day / 3600,
        (seconds_of_day / 60) % 60,
        seconds_of_day % 60
    )
}

/// `yyyy-MM-ddTHH:mm:ssZ` for now — the collector's own ISO form for a whole second.
pub fn utc_iso8601_now() -> String {
    utc_iso8601(now_unix_seconds())
}

fn utc_iso8601(unix_seconds: i64) -> String {
    format!("{}Z", utc_timestamp(unix_seconds).replacen(' ', "T", 1))
}

fn utc_date(unix_seconds: i64) -> String {
    let (year, month, day) = civil_from_unix_days(unix_seconds.div_euclid(86_400));
    format!("{year:04}-{month:02}-{day:02}")
}

/// Days since the Unix epoch -> (year, month, day). Howard Hinnant's `civil_from_days`, which is
/// exact for the whole proleptic Gregorian range and needs no date library.
fn civil_from_unix_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let day_of_era = z - era * 146_097; // [0, 146096]
    let year_of_era =
        (day_of_era - day_of_era / 1460 + day_of_era / 36_524 - day_of_era / 146_096) / 365; // [0, 399]
    let year = year_of_era + era * 400;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100); // [0, 365]
    let mp = (5 * day_of_year + 2) / 153; // [0, 11] with March = 0
    let day = (day_of_year - (153 * mp + 2) / 5 + 1) as u32; // [1, 31]
    let month = (if mp < 10 { mp + 3 } else { mp - 9 }) as u32; // [1, 12]
    (if month <= 2 { year + 1 } else { year }, month, day)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_the_epoch() {
        assert_eq!(utc_timestamp(0), "1970-01-01 00:00:00");
        assert_eq!(utc_date(0), "1970-01-01");
    }

    #[test]
    fn formats_known_instants() {
        // A recent instant, a leap day, and the last second before a century rollover.
        assert_eq!(utc_timestamp(1_790_430_307), "2026-09-26 13:45:07");
        assert_eq!(utc_timestamp(1_709_164_800), "2024-02-29 00:00:00");
        assert_eq!(utc_timestamp(946_684_799), "1999-12-31 23:59:59");
    }

    #[test]
    fn the_log_file_rolls_when_the_utc_date_changes() {
        // A daemon that runs for months must not write one unbounded file.
        let directory = std::env::temp_dir().join(format!(
            "hsm-probe-log-test-{}-{}",
            std::process::id(),
            now_unix_seconds()
        ));
        let mut file = LogFile {
            directory: directory.clone(),
            date: "2026-09-22".to_string(),
            handle: open_log_file(&directory, "2026-09-22").expect("open"),
        };

        file.roll_if_needed("2026-09-22");
        assert_eq!(file.date, "2026-09-22", "the same date must not reopen");

        file.roll_if_needed("2026-09-23");
        assert_eq!(file.date, "2026-09-23");
        assert!(directory.join(log_file_name("2026-09-22")).exists());
        assert!(directory.join(log_file_name("2026-09-23")).exists());

        let _ = std::fs::remove_dir_all(&directory);
    }

    #[test]
    fn formats_iso8601() {
        assert_eq!(utc_iso8601(0), "1970-01-01T00:00:00Z");
        assert_eq!(utc_iso8601(1_709_164_800), "2024-02-29T00:00:00Z");
    }

    #[test]
    fn level_names_map_to_levels() {
        assert_eq!(parse_level("debug"), Level::Debug);
        assert_eq!(parse_level("INFO"), Level::Info);
        assert_eq!(parse_level("warn"), Level::Warn);
        assert_eq!(parse_level("Error"), Level::Error);
        assert_eq!(parse_level("nonsense"), Level::Info);
    }

    #[test]
    fn values_dropped_at_stop_are_raised_to_warn() {
        // The exact text the native collector emits at debug on a bounded stop drain.
        let dropped = "Collector stop dropped 34 pending value(s): transport unavailable.";
        assert_eq!(
            collector_message_level(LogLevel::Debug, dropped),
            Level::Warn
        );
        assert_eq!(
            collector_message_level(LogLevel::Info, dropped),
            Level::Warn
        );
        // Never downgraded: an error stays an error.
        assert_eq!(
            collector_message_level(LogLevel::Error, dropped),
            Level::Error
        );
    }

    #[test]
    fn nothing_dropped_or_other_messages_keep_the_collector_level() {
        let none = "Collector stop dropped 0 pending value(s): transport unavailable.";
        assert_eq!(collector_message_level(LogLevel::Debug, none), Level::Debug);
        assert_eq!(
            collector_message_level(LogLevel::Debug, "Collector stop dropped x"),
            Level::Debug
        );
        assert_eq!(
            collector_message_level(LogLevel::Info, "DataCollector -> Stopped"),
            Level::Info
        );
        assert_eq!(
            collector_message_level(LogLevel::Error, "Failed to send 9 value(s)"),
            Level::Error
        );
    }
}
