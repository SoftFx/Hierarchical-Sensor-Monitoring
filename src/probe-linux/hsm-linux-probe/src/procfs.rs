//! `/proc` readers for the probe-added sensors of this slice.
//!
//! These signals exist in no collector today (initiative §4.2), which is why the probe owns them.
//! Anything the collector's default catalog already reports stays in the collector — adding a
//! second implementation here would be exactly the divergence rules #9/#10 forbid.
//!
//! The parsers are pure functions over text so they are testable on any OS; only the thin
//! `read_*` wrappers touch the filesystem.

use std::fmt;

pub const LOADAVG_PATH: &str = "/proc/loadavg";
pub const CPUINFO_PATH: &str = "/proc/cpuinfo";

/// The three kernel load averages.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LoadAverage {
    pub one: f64,
    pub five: f64,
    pub fifteen: f64,
}

#[derive(Debug)]
pub enum ProcError {
    Io {
        path: &'static str,
        source: std::io::Error,
    },
    Malformed {
        path: &'static str,
        reason: String,
    },
}

impl ProcError {
    fn malformed(path: &'static str, reason: impl Into<String>) -> Self {
        ProcError::Malformed {
            path,
            reason: reason.into(),
        }
    }
}

impl fmt::Display for ProcError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ProcError::Io { path, source } => write!(f, "cannot read {path}: {source}"),
            ProcError::Malformed { path, reason } => write!(f, "cannot parse {path}: {reason}"),
        }
    }
}

impl std::error::Error for ProcError {}

/// Parse the first three whitespace-separated fields of `/proc/loadavg`.
///
/// The remaining fields (running/total tasks, last PID) are deliberately ignored: they are not part
/// of this slice, and tolerating their absence keeps the parser working against the trimmed files
/// some container runtimes expose.
pub fn parse_loadavg(contents: &str) -> Result<LoadAverage, ProcError> {
    let mut fields = contents.split_ascii_whitespace();
    let mut next = |name: &str| -> Result<f64, ProcError> {
        let raw = fields.next().ok_or_else(|| {
            ProcError::malformed(LOADAVG_PATH, format!("missing the {name} field"))
        })?;
        let value: f64 = raw.parse().map_err(|_| {
            ProcError::malformed(
                LOADAVG_PATH,
                format!("{name} field '{raw}' is not a number"),
            )
        })?;
        if !value.is_finite() || value < 0.0 {
            return Err(ProcError::malformed(
                LOADAVG_PATH,
                format!("{name} field '{raw}' is not a valid load average"),
            ));
        }
        Ok(value)
    };

    Ok(LoadAverage {
        one: next("1-minute")?,
        five: next("5-minute")?,
        fifteen: next("15-minute")?,
    })
}

/// Count the logical CPUs the host exposes by counting `processor` entries in `/proc/cpuinfo`.
///
/// Deliberately not `available_parallelism()`: that reports the *usable* parallelism after cgroup
/// quota and CPU affinity, while the sensor's job (§4.2) is to report host capacity so an operator
/// can read container CPU percentages against it.
pub fn parse_logical_cores(contents: &str) -> Result<i32, ProcError> {
    let count = contents
        .lines()
        .filter(|line| {
            let mut parts = line.splitn(2, ':');
            matches!(parts.next(), Some(key) if key.trim() == "processor") && parts.next().is_some()
        })
        .count();

    if count == 0 {
        return Err(ProcError::malformed(
            CPUINFO_PATH,
            "no 'processor' entries found",
        ));
    }
    i32::try_from(count)
        .map_err(|_| ProcError::malformed(CPUINFO_PATH, "implausible processor count"))
}

/// The kernel's `TASK_COMM_LEN` minus the NUL: `comm` is truncated to this many bytes.
const MAX_COMM_LEN: usize = 15;

/// This process's name as .NET's `Process.GetCurrentProcess().ProcessName` computes it on Linux,
/// which is what the managed collector puts in the `Process <name>` node
/// (`ProcessCollectionPrototypes`), so both collectors name the node identically.
///
/// .NET reads `comm` from `/proc/<pid>/stat`. Because the kernel truncates `comm` to 15 bytes, when
/// it is exactly that long .NET recovers the full name from `argv[0]`'s basename, provided that
/// basename starts with `comm` (`Process.Linux.cs`, `GetUntruncatedProcessName`); otherwise `comm`
/// stands. Unlike `current_exe()`, this cannot pick up the `" (deleted)"` suffix `/proc/self/exe`
/// carries after a package upgrade replaced the binary under a running daemon.
pub fn process_name(comm: &str, cmdline: &[u8]) -> Option<String> {
    let comm = comm.trim_end_matches('\n');
    if comm.is_empty() {
        return None;
    }
    if comm.len() >= MAX_COMM_LEN {
        let argv0 = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
        let argv0 = String::from_utf8_lossy(argv0);
        let basename = argv0.rsplit('/').next().unwrap_or_default();
        if basename.starts_with(comm) {
            return Some(basename.to_string());
        }
    }
    Some(comm.to_string())
}

/// [`process_name`] for this process. `None` if `/proc` cannot be read, in which case the
/// collector falls back to its `Process process` placeholder.
pub fn current_process_name() -> Option<String> {
    let comm = std::fs::read_to_string("/proc/self/comm").ok()?;
    let cmdline = std::fs::read("/proc/self/cmdline").unwrap_or_default();
    process_name(&comm, &cmdline)
}

pub fn read_loadavg() -> Result<LoadAverage, ProcError> {
    let contents = std::fs::read_to_string(LOADAVG_PATH).map_err(|source| ProcError::Io {
        path: LOADAVG_PATH,
        source,
    })?;
    parse_loadavg(&contents)
}

pub fn read_logical_cores() -> Result<i32, ProcError> {
    let contents = std::fs::read_to_string(CPUINFO_PATH).map_err(|source| ProcError::Io {
        path: CPUINFO_PATH,
        source,
    })?;
    parse_logical_cores(&contents)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_a_real_loadavg_line() {
        let load = parse_loadavg("0.52 0.58 0.59 1/1234 5678\n").expect("parse");
        assert_eq!(
            load,
            LoadAverage {
                one: 0.52,
                five: 0.58,
                fifteen: 0.59
            }
        );
    }

    #[test]
    fn parses_without_the_trailing_fields() {
        let load = parse_loadavg("1.00 2.00 3.00").expect("parse");
        assert_eq!(
            load,
            LoadAverage {
                one: 1.0,
                five: 2.0,
                fifteen: 3.0
            }
        );
    }

    #[test]
    fn parses_a_heavily_loaded_host() {
        let load = parse_loadavg("128.75 96.10 40.00 12/900 4242").expect("parse");
        assert_eq!(load.one, 128.75);
    }

    #[test]
    fn short_input_is_rejected() {
        for text in ["", "\n", "0.10", "0.10 0.20", "0.10 0.20 \n"] {
            let error = parse_loadavg(text).expect_err("short input must fail");
            assert!(matches!(error, ProcError::Malformed { .. }), "{error}");
        }
    }

    #[test]
    fn malformed_input_is_rejected() {
        for text in [
            "a b c",
            "0.1 x 0.3",
            "nan 0.2 0.3",
            "-1.0 0.2 0.3",
            "inf 0.2 0.3",
        ] {
            let error = parse_loadavg(text).expect_err("malformed input must fail");
            assert!(matches!(error, ProcError::Malformed { .. }), "{error}");
        }
    }

    #[test]
    fn a_malformed_loadavg_never_yields_a_substitute_value() {
        // "No silent data loss" (root CLAUDE.md #8) cuts both ways: an unreadable source must
        // surface as an error, never as a plausible-looking zero.
        assert!(parse_loadavg("garbage").is_err());
    }

    #[test]
    fn counts_processor_entries_in_cpuinfo() {
        let cpuinfo = "processor\t: 0\nvendor_id\t: GenuineIntel\n\nprocessor\t: 1\nvendor_id\t: GenuineIntel\n\n\
                       processor\t: 2\n\nprocessor\t: 3\n";
        assert_eq!(parse_logical_cores(cpuinfo).expect("parse"), 4);
    }

    #[test]
    fn ignores_keys_that_merely_start_with_processor() {
        let cpuinfo = "processor\t: 0\nprocessor type\t: x\n";
        assert_eq!(parse_logical_cores(cpuinfo).expect("parse"), 1);
    }

    #[test]
    fn the_probe_binary_is_named_after_its_executable() {
        // "hsm-linux-probe" is exactly 15 bytes, so comm is at the truncation limit and the
        // argv[0] branch is the one that decides — as it will be on the host.
        assert_eq!(
            process_name(
                "hsm-linux-probe\n",
                b"/usr/bin/hsm-linux-probe\0--config\0/etc/c.json\0"
            )
            .as_deref(),
            Some("hsm-linux-probe")
        );
    }

    #[test]
    fn a_truncated_comm_is_recovered_from_argv0() {
        assert_eq!(
            process_name("a-very-long-bin", b"/opt/x/a-very-long-binary-name\0").as_deref(),
            Some("a-very-long-binary-name")
        );
    }

    #[test]
    fn comm_stands_when_argv0_does_not_extend_it() {
        // Short comm: never consults argv[0].
        assert_eq!(
            process_name("probe\n", b"/usr/bin/other\0").as_deref(),
            Some("probe")
        );
        // Truncated comm, but argv[0] was rewritten to something unrelated.
        assert_eq!(
            process_name("a-very-long-bin", b"renamed\0").as_deref(),
            Some("a-very-long-bin")
        );
        // Truncated comm, no cmdline at all (kernel threads, zombies).
        assert_eq!(
            process_name("a-very-long-bin", b"").as_deref(),
            Some("a-very-long-bin")
        );
    }

    #[test]
    fn an_empty_comm_yields_no_name() {
        assert_eq!(process_name("", b"/usr/bin/x\0"), None);
        assert_eq!(process_name("\n", b""), None);
    }

    #[test]
    fn cpuinfo_without_processor_entries_is_rejected() {
        for text in ["", "model name\t: something\n", "processor\n"] {
            assert!(
                parse_logical_cores(text).is_err(),
                "expected an error for {text:?}"
            );
        }
    }
}
