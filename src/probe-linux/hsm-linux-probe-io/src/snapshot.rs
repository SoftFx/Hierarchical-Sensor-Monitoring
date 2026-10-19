//! The snapshot file: what the helper read in one scan, for the probe.
//!
//! ```text
//! hsm-linux-probe-io 2 seq=42 mono_ns=123456789000 wall_ms=1791900000000 kind=regular
//! 1 2 0 0 0 1048576 52428800 1048576 50331648 92114 systemd
//! 812 9120 1 0 1 - - - - 0 cron%20job
//! 4410 9500 1 0 0 0 734003200 0 0 0 valheim_server.x86_64
//! ```
//!
//! The header: the format name and version, the scan's sequence number (from 1 at each helper
//! start), its `CLOCK_MONOTONIC` time in ns (how old it is, whatever the wall clock does), its wall
//! time in Unix ms (which local day it counts towards) and its kind — `regular` (every
//! `periodMs`) or `final` (the extra scan in the day's last 30 s). Then one line per process:
//! `pid start_ticks ppid kernel_thread sigchld_ignored read_bytes write_bytes reaped_read
//! reaped_write cminflt name`:
//!
//! * `read_bytes write_bytes` — `/proc/<pid>/io`, the whole process; `-` when it could not be read;
//! * `reaped_read reaped_write` — the part of them that came from **exited threads and reaped
//!   children** (the kernel's `signal->ioac`): the whole minus the sum of the live threads'
//!   `/proc/<pid>/task/<tid>/io`; `-` when unknown (a kernel thread, a thread read refused);
//! * `cminflt` — stat field 11, the reaped children's minor faults: it grows with every reap, which
//!   tells a reap from an exited thread (#1572);
//! * `name` — `comm`, or the full name the `exe` link gives a 15-byte `comm`
//!   ([`crate::procstat::extended_name`], #1573); last and percent-escaped (every byte outside
//!   `!`..`~`, and `%` itself, as `%XX`), so a name holding spaces or newlines cannot break a line.
//!
//! Version 1 (helper ≤ 0.9.1) had no `reaped_*`/`cminflt` fields and always the plain `comm`; the
//! reader still accepts it (the reaped part unknown, `cminflt` 0), so a probe upgraded before its
//! helper restarts keeps working. A reader rejects another format name or version.

use std::fmt::Write as _;
use std::io::{self, Write as _};
use std::path::{Path, PathBuf};

pub const FORMAT: &str = "hsm-linux-probe-io";
/// The version written.
pub const VERSION: u32 = 2;
/// The versions read: 1 (no reaped part, plain `comm`) and 2.
pub const READABLE_VERSIONS: [u32; 2] = [1, 2];
/// Where the helper's unit (`RuntimeDirectory=hsm-linux-probe-io`) keeps it.
pub const DEFAULT_PATH: &str = "/run/hsm-linux-probe-io/snapshot";

/// The two counters of `/proc/<pid>/io` the sensors use.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct IoCounters {
    pub read_bytes: u64,
    pub write_bytes: u64,
}

/// Parse `/proc/<pid>/io` (`key: value` lines): `read_bytes` and `write_bytes` — block I/O, not
/// `rchar`/`wchar`. `None` when either is missing or not a number.
pub fn parse_io(text: &str) -> Option<IoCounters> {
    let (mut read_bytes, mut write_bytes) = (None, None);
    for line in text.lines() {
        let Some((key, value)) = line.split_once(':') else {
            continue;
        };
        match key.trim() {
            "read_bytes" => read_bytes = value.trim().parse().ok(),
            "write_bytes" => write_bytes = value.trim().parse().ok(),
            _ => {}
        }
    }
    Some(IoCounters {
        read_bytes: read_bytes?,
        write_bytes: write_bytes?,
    })
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Kind {
    /// Every `periodMs`: the speeds are posted from it.
    Regular,
    /// The extra scan in the day's last 30 seconds: the day's totals are posted from it.
    Final,
}

/// One process of a snapshot.
#[derive(Clone, Debug, PartialEq)]
pub struct Entry {
    pub pid: u32,
    pub start_ticks: u64,
    pub ppid: u32,
    pub kernel_thread: bool,
    pub sigchld_ignored: bool,
    /// `None`: its `io` could not be read.
    pub counters: Option<IoCounters>,
    /// The part of `counters` from exited threads and reaped children; `None`: unknown.
    pub reaped: Option<IoCounters>,
    /// Stat field 11, the reaped children's minor faults (0 in a version-1 snapshot).
    pub cminflt: u64,
    /// `comm`, or the full name of a 15-byte `comm` (`procstat::extended_name`).
    pub name: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Snapshot {
    pub seq: u64,
    pub mono_ns: u64,
    pub wall_ms: i64,
    pub kind: Kind,
    pub entries: Vec<Entry>,
}

impl Snapshot {
    pub fn render(&self) -> String {
        let kind = match self.kind {
            Kind::Regular => "regular",
            Kind::Final => "final",
        };
        let mut text = format!(
            "{FORMAT} {VERSION} seq={} mono_ns={} wall_ms={} kind={kind}\n",
            self.seq, self.mono_ns, self.wall_ms
        );
        let pair = |counters: Option<IoCounters>| match counters {
            Some(c) => format!("{} {}", c.read_bytes, c.write_bytes),
            None => "- -".to_string(),
        };
        for entry in &self.entries {
            let _ = writeln!(
                text,
                "{} {} {} {} {} {} {} {} {}",
                entry.pid,
                entry.start_ticks,
                entry.ppid,
                u8::from(entry.kernel_thread),
                u8::from(entry.sigchld_ignored),
                pair(entry.counters),
                pair(entry.reaped),
                entry.cminflt,
                escape(&entry.name)
            );
        }
        text
    }

    pub fn parse(text: &str) -> Result<Snapshot, String> {
        let mut lines = text.lines();
        let header = lines.next().ok_or("empty snapshot")?;
        let mut fields = header.split_whitespace();
        let version = match (fields.next(), fields.next()) {
            (Some(FORMAT), Some(version)) => version
                .parse::<u32>()
                .ok()
                .filter(|version| READABLE_VERSIONS.contains(version)),
            _ => None,
        }
        .ok_or_else(|| format!("not a {FORMAT} 1 or 2 snapshot: '{header}'"))?;
        let mut value = |key: &str| -> Result<&str, String> {
            fields
                .next()
                .and_then(|field| field.strip_prefix(key))
                .and_then(|field| field.strip_prefix('='))
                .ok_or_else(|| format!("snapshot header lacks {key}: '{header}'"))
        };
        let number = |text: &str| text.parse::<i64>().map_err(|e| format!("{text}: {e}"));
        let seq = number(value("seq")?)?;
        let mono_ns = number(value("mono_ns")?)?;
        let wall_ms = number(value("wall_ms")?)?;
        let kind = match value("kind")? {
            "regular" => Kind::Regular,
            "final" => Kind::Final,
            other => return Err(format!("unknown snapshot kind '{other}'")),
        };
        let mut entries = Vec::new();
        for (index, line) in lines.enumerate() {
            entries
                .push(parse_entry(line, version).ok_or_else(|| {
                    format!("snapshot line {} is malformed: '{line}'", index + 2)
                })?);
        }
        Ok(Snapshot {
            seq: u64::try_from(seq).map_err(|e| e.to_string())?,
            mono_ns: u64::try_from(mono_ns).map_err(|e| e.to_string())?,
            wall_ms,
            kind,
            entries,
        })
    }
}

/// One process line of a `version` snapshot; `None` when malformed.
fn parse_entry(line: &str, version: u32) -> Option<Entry> {
    let mut fields = line.split(' ');
    let mut next = || fields.next();
    let pid = next()?.parse().ok()?;
    let start_ticks = next()?.parse().ok()?;
    let ppid = next()?.parse().ok()?;
    let flag = |text: &str| match text {
        "0" => Some(false),
        "1" => Some(true),
        _ => None,
    };
    let kernel_thread = flag(next()?)?;
    let sigchld_ignored = flag(next()?)?;
    let pair = |read: &str, write: &str| -> Option<Option<IoCounters>> {
        match (read, write) {
            ("-", "-") => Some(None),
            (read, write) => Some(Some(IoCounters {
                read_bytes: read.parse().ok()?,
                write_bytes: write.parse().ok()?,
            })),
        }
    };
    let counters = pair(next()?, next()?)?;
    let (reaped, cminflt) = if version >= 2 {
        (pair(next()?, next()?)?, next()?.parse().ok()?)
    } else {
        (None, 0)
    };
    let name = unescape(next()?)?;
    if next().is_some() {
        return None;
    }
    Some(Entry {
        pid,
        start_ticks,
        ppid,
        kernel_thread,
        sigchld_ignored,
        counters,
        reaped,
        cminflt,
        name,
    })
}

fn escape(name: &str) -> String {
    let mut out = String::with_capacity(name.len());
    for byte in name.bytes() {
        if (b'!'..=b'~').contains(&byte) && byte != b'%' {
            out.push(char::from(byte));
        } else {
            let _ = write!(out, "%{byte:02X}");
        }
    }
    out
}

fn unescape(text: &str) -> Option<String> {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' {
            let hex = text.get(index + 1..index + 3)?;
            out.push(u8::from_str_radix(hex, 16).ok()?);
            index += 3;
        } else {
            out.push(bytes[index]);
            index += 1;
        }
    }
    Some(String::from_utf8_lossy(&out).into_owned())
}

/// Replace `path` with `text` atomically (a temp file next to it, then a rename), so a reader
/// never sees half a snapshot.
pub fn write_atomic(path: &Path, text: &str) -> io::Result<()> {
    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(".tmp");
    let temp: PathBuf = path.with_file_name(name);
    let written = std::fs::File::create(&temp)
        .and_then(|mut out| out.write_all(text.as_bytes()))
        .and_then(|()| std::fs::rename(&temp, path));
    // A failed write (a full tmpfs) or rename leaves no temp file behind.
    written.inspect_err(|_| {
        let _ = std::fs::remove_file(&temp);
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample() -> Snapshot {
        Snapshot {
            seq: 42,
            mono_ns: 123_456_789_000,
            wall_ms: 1_791_900_000_000,
            kind: Kind::Final,
            entries: vec![
                Entry {
                    pid: 1,
                    start_ticks: 2,
                    ppid: 0,
                    kernel_thread: false,
                    sigchld_ignored: false,
                    counters: Some(IoCounters {
                        read_bytes: 1_048_576,
                        write_bytes: 52_428_800,
                    }),
                    reaped: Some(IoCounters {
                        read_bytes: 1_048_576,
                        write_bytes: 50_331_648,
                    }),
                    cminflt: 92_114,
                    name: "systemd".into(),
                },
                Entry {
                    pid: 812,
                    start_ticks: 9120,
                    ppid: 1,
                    kernel_thread: true,
                    sigchld_ignored: true,
                    counters: None,
                    reaped: None,
                    cminflt: 0,
                    name: "a b\nc%d) (e ü".into(),
                },
            ],
        }
    }

    #[test]
    fn a_snapshot_round_trips_whatever_the_names_hold() {
        let text = sample().render();
        assert!(text.starts_with(
            "hsm-linux-probe-io 2 seq=42 mono_ns=123456789000 wall_ms=1791900000000 kind=final\n"
        ));
        assert!(
            text.contains("\n1 2 0 0 0 1048576 52428800 1048576 50331648 92114 systemd\n"),
            "{text}"
        );
        assert!(
            text.contains("\n812 9120 1 1 1 - - - - 0 a%20b%0Ac%25d)%20(e%20%C3%BC\n"),
            "{text}"
        );
        assert_eq!(text.lines().count(), 3);
        assert_eq!(Snapshot::parse(&text), Ok(sample()));
    }

    /// What a 0.9.x helper writes is still read: no reaped part (unknown), `cminflt` 0.
    #[test]
    fn a_version_1_snapshot_is_still_read() {
        let text = "hsm-linux-probe-io 1 seq=7 mono_ns=5 wall_ms=6 kind=regular\n\
                    1 2 0 0 0 1048576 52428800 systemd\n\
                    812 9120 1 1 1 - - cron%20job\n";
        let snapshot = Snapshot::parse(text).expect("version 1");
        assert_eq!(snapshot.entries.len(), 2);
        let systemd = &snapshot.entries[0];
        assert_eq!(
            (systemd.reaped, systemd.cminflt, systemd.name.as_str()),
            (None, 0, "systemd")
        );
        assert_eq!(systemd.counters.map(|c| c.write_bytes), Some(52_428_800));
        assert_eq!(snapshot.entries[1].name, "cron job");
        // A version-2 line under a version-1 header (or the reverse) is malformed.
        let mixed = text.replace("cron%20job", "- - 0 cron%20job");
        assert!(Snapshot::parse(&mixed).is_err());
        let v2_short = text.replace("hsm-linux-probe-io 1", "hsm-linux-probe-io 2");
        assert!(Snapshot::parse(&v2_short).is_err());
    }

    #[test]
    fn a_foreign_or_broken_file_is_rejected() {
        let good = sample().render();
        for bad in [
            String::new(),
            good.replace("hsm-linux-probe-io 2", "hsm-linux-probe-io 3"),
            good.replace("hsm-linux-probe-io 2", "hsm-linux-probe-io 0"),
            good.replace("hsm-linux-probe-io 2", "other-format 2"),
            good.replace(" 92114 ", " x "),
            good.replace(" 50331648 ", " - "),
            good.replace("kind=final", "kind=other"),
            good.replace("seq=42 ", ""),
            good.replace(" systemd", " sys temd"),
            good.replace(" 52428800 ", " - "),
            good.replace("%C3%BC", "%C"),
            good.replace("%25", "%zz"),
        ] {
            assert!(Snapshot::parse(&bad).is_err(), "{bad}");
        }
    }

    #[test]
    fn io_is_parsed_from_the_kernel_layout() {
        let text = "rchar: 323934931\nwchar: 323929600\nsyscr: 632687\nsyscw: 632675\n\
                    read_bytes: 0\nwrite_bytes: 323932160\ncancelled_write_bytes: 0\n";
        assert_eq!(
            parse_io(text),
            Some(IoCounters {
                read_bytes: 0,
                write_bytes: 323_932_160
            })
        );
        // wchar/rchar are never taken for the block counters.
        assert_eq!(parse_io("rchar: 5\nwchar: 7\n"), None);
        assert_eq!(parse_io("read_bytes: 1\nwrite_bytes: x\n"), None);
    }

    #[test]
    fn the_write_is_atomic_and_replaces() {
        let dir = std::env::temp_dir().join(format!("hsm-io-snap-{}", std::process::id()));
        std::fs::create_dir_all(&dir).expect("mkdir");
        let path = dir.join("snapshot");
        write_atomic(&path, "one").expect("write");
        write_atomic(&path, "two").expect("write");
        assert_eq!(std::fs::read_to_string(&path).expect("read"), "two");
        assert!(!dir.join("snapshot.tmp").exists());
        std::fs::remove_dir_all(&dir).expect("rm");
    }
}
