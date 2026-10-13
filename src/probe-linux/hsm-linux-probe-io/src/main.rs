//! `hsm-linux-probe-io`: the root-run helper of top disk I/O (#1547).
//!
//! Every `topDiskIo.periodMs` (from the probe's config; 60 s by default) it scans `/proc` and
//! replaces `/run/hsm-linux-probe-io/snapshot` atomically; once a day it adds a `final` scan in
//! the day's last 30 seconds, for the per-day totals. It exits at once (0) when `topDiskIo` is not
//! enabled. No network, no other input. Logs go to stderr (the journal).
//!
//! ```text
//! hsm-linux-probe-io [--config <path>] [--output <path>] [--once]
//! ```

use std::path::PathBuf;
use std::process::ExitCode;
use std::time::Duration;

use hsm_linux_probe_io::clock::{local_time, monotonic_ns, unix_now_ms};
use hsm_linux_probe_io::scan::Scanner;
use hsm_linux_probe_io::snapshot::{write_atomic, Kind, Snapshot, DEFAULT_PATH};

const DEFAULT_CONFIG: &str = "/etc/hsm-linux-probe/config.json";
const DEFAULT_PERIOD_MS: u64 = 60_000;
/// The final scan's window: the day's last this many seconds; a 5-s tick always lands in it.
const FINAL_WINDOW_SECONDS: i64 = 30;
const TICK: Duration = Duration::from_secs(5);

struct Args {
    config: PathBuf,
    output: PathBuf,
    once: bool,
}

fn parse_args() -> Result<Args, String> {
    let mut args = Args {
        config: PathBuf::from(DEFAULT_CONFIG),
        output: PathBuf::from(DEFAULT_PATH),
        once: false,
    };
    let mut list = std::env::args().skip(1);
    while let Some(arg) = list.next() {
        match arg.as_str() {
            "--config" => args.config = list.next().ok_or("--config needs a path")?.into(),
            "--output" => args.output = list.next().ok_or("--output needs a path")?.into(),
            "--once" => args.once = true,
            "--version" => {
                println!("hsm-linux-probe-io {}", env!("CARGO_PKG_VERSION"));
                std::process::exit(0);
            }
            other => return Err(format!("unknown argument '{other}'")),
        }
    }
    Ok(args)
}

/// `(enabled, periodMs)` of the top-level `topDiskIo` block — the only keys read.
fn top_disk_io(config: &str) -> Result<(bool, u64), String> {
    let json: serde_json::Value =
        serde_json::from_str(config).map_err(|error| format!("not JSON: {error}"))?;
    let block = &json["topDiskIo"];
    let enabled = block["enabled"].as_bool().unwrap_or(false);
    let period = match &block["periodMs"] {
        serde_json::Value::Null => DEFAULT_PERIOD_MS,
        value => value
            .as_u64()
            .filter(|ms| *ms > 0)
            .ok_or_else(|| format!("topDiskIo.periodMs must be a number > 0 (got {value})"))?,
    };
    Ok((enabled, period))
}

fn main() -> ExitCode {
    let args = match parse_args() {
        Ok(args) => args,
        Err(error) => {
            eprintln!("hsm-linux-probe-io: {error}");
            return ExitCode::from(2);
        }
    };
    let config = match std::fs::read_to_string(&args.config) {
        Ok(text) => text,
        Err(error) => {
            eprintln!(
                "hsm-linux-probe-io: cannot read {}: {error}",
                args.config.display()
            );
            return ExitCode::FAILURE;
        }
    };
    let (enabled, period_ms) = match top_disk_io(&config) {
        Ok(settings) => settings,
        Err(error) => {
            eprintln!("hsm-linux-probe-io: {}: {error}", args.config.display());
            return ExitCode::FAILURE;
        }
    };
    if !enabled {
        eprintln!(
            "hsm-linux-probe-io: topDiskIo is not enabled in {}; nothing to do",
            args.config.display()
        );
        return ExitCode::SUCCESS;
    }
    let period = Duration::from_millis(period_ms);
    eprintln!(
        "hsm-linux-probe-io {}: every {} s (and once in the day's last {FINAL_WINDOW_SECONDS} s) \
         -> {}",
        env!("CARGO_PKG_VERSION"),
        period.as_secs_f64(),
        args.output.display()
    );
    run(&args, period)
}

fn run(args: &Args, period: Duration) -> ExitCode {
    let mut scanner = Scanner::new("/proc");
    let mut seq = 0u64;
    let mut next_regular = monotonic_ns();
    let mut final_day: Option<i64> = None;
    let mut last_error: Option<String> = None;
    let mut denied_logged = false;
    let tick = TICK.min(period);
    loop {
        let now = monotonic_ns();
        let wall_ms = unix_now_ms();
        let (day, second) = local_time(wall_ms);
        let regular = now >= next_regular;
        let in_window = second + FINAL_WINDOW_SECONDS >= 86_400;
        let final_due = in_window && final_day != Some(day);
        if regular || final_due {
            if regular {
                let period_ns = u64::try_from(period.as_nanos()).unwrap_or(u64::MAX);
                next_regular = next_regular.saturating_add(period_ns);
                if next_regular <= now {
                    next_regular = now.saturating_add(period_ns);
                }
            }
            if in_window {
                final_day = Some(day);
            }
            seq += 1;
            let kind = if regular { Kind::Regular } else { Kind::Final };
            let outcome = scanner.scan().and_then(|scan| {
                if scan.denied > 0 && !denied_logged {
                    eprintln!(
                        "hsm-linux-probe-io: WARNING: /proc/<pid>/io of {} processes refused \
                         (EACCES): the unit must keep CAP_DAC_READ_SEARCH and CAP_SYS_PTRACE",
                        scan.denied
                    );
                    denied_logged = true;
                }
                let snapshot = Snapshot {
                    seq,
                    mono_ns: monotonic_ns(),
                    wall_ms,
                    kind,
                    entries: scan.entries,
                };
                write_atomic(&args.output, &snapshot.render())
                    .map_err(|error| format!("cannot write {}: {error}", args.output.display()))
            });
            match outcome {
                Ok(()) => {
                    if last_error.take().is_some() {
                        eprintln!("hsm-linux-probe-io: recovered");
                    }
                }
                Err(error) => {
                    if last_error.as_deref() != Some(error.as_str()) {
                        eprintln!("hsm-linux-probe-io: ERROR: {error} (repeats are not logged)");
                        last_error = Some(error);
                    }
                }
            }
            if args.once {
                return if last_error.is_some() {
                    ExitCode::FAILURE
                } else {
                    ExitCode::SUCCESS
                };
            }
        }
        std::thread::sleep(tick);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_the_top_level_block_is_read() {
        assert_eq!(top_disk_io("{}"), Ok((false, DEFAULT_PERIOD_MS)));
        assert_eq!(
            top_disk_io(r#"{"topDiskIo": {"enabled": true, "periodMs": 30000}}"#),
            Ok((true, 30_000))
        );
        assert_eq!(
            top_disk_io(r#"{"probe": {"topDiskIo": {"enabled": true}}}"#),
            Ok((false, DEFAULT_PERIOD_MS))
        );
        assert!(top_disk_io(r#"{"topDiskIo": {"enabled": true, "periodMs": 0}}"#).is_err());
        assert!(top_disk_io("not json").is_err());
    }
}
