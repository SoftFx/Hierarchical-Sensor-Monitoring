//! Per-process disk I/O sampling from `/proc` (#1547): the `io` parsing, the per-process deltas,
//! the reaped-child correction, the name aggregation and the top-N selection. Pure over an
//! injectable proc root, so every rule is tested on a fixture tree; nothing here touches the
//! collector.
//!
//! # What one read reads
//!
//! The process table exactly as the top-CPU sampler reads it ([`read_processes`]: one
//! `<proc>/<pid>/stat` per numeric entry, which gives the name, the parent and the start time) and,
//! for each of those processes, `<proc>/<pid>/io`. Nothing outside `/proc` is read: a sleeping disk
//! is never asked.
//!
//! # The counters
//!
//! `write_bytes` and `read_bytes` of `/proc/<pid>/io` — the bytes the process caused to be sent to
//! or fetched from the block layer — not `wchar`/`rchar`, which count every `write(2)`/`read(2)`
//! (sockets, pipes, the page cache) and say nothing about the disk. `write_bytes` is charged when
//! the process dirties a page, so data flushed later by the kernel still counts for the process
//! that wrote it; `cancelled_write_bytes` (dirty pages truncated before they reached the disk) is
//! **not** subtracted, as `iotop` and `pidstat -d` do not either. For a process the kernel sums
//! every thread, live and exited.
//!
//! # Reaped children
//!
//! The kernel also adds the counters of a child the process has reaped (`wait_task_zombie` adds the
//! child's whole `ioac` to the parent's `signal->ioac`, which `/proc/<pid>/io` includes). So when a
//! process the sampler has been measuring exits, its reaper's counter jumps by the child's
//! **whole-life** total, which would count everything already credited to the child a second time
//! under the reaper's name (`cron`, `bash`, `systemd`, `containerd-shim`). [`IoSampler::read`]
//! therefore subtracts, from the delta of the receiving process, each vanished process's counters
//! at the previous read. What is left is the vanished process's last partial interval, which the
//! kernel moved to the receiver — it is credited there (see [`receiver`] for who that is).

use std::collections::{BTreeMap, HashMap};
use std::io::ErrorKind;
use std::path::{Path, PathBuf};

use crate::probe_only::top_cpu::procfs::{read_processes, sensor_name};

/// The two counters of `/proc/<pid>/io` the sampler uses.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct IoCounters {
    pub read_bytes: u64,
    pub write_bytes: u64,
}

/// Parse `/proc/<pid>/io` (`key: value` lines); `None` when either counter is missing or not a
/// number.
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

/// Which counter: the writers' or the readers'.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Direction {
    Write,
    Read,
}

/// One name's bytes over an interval (or over a day), summed over every process of the name.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct NameIo {
    pub written: u64,
    pub read: u64,
    /// The process of the name with the largest write delta, and that delta: the one whose
    /// executable a writers' sensor description names.
    pub top_writer: (u32, u64),
    /// The same for the reads.
    pub top_reader: (u32, u64),
    pub kernel_thread: bool,
}

impl NameIo {
    pub fn bytes(&self, direction: Direction) -> u64 {
        match direction {
            Direction::Write => self.written,
            Direction::Read => self.read,
        }
    }

    /// The representative pid for `direction`.
    pub fn pid(&self, direction: Direction) -> u32 {
        match direction {
            Direction::Write => self.top_writer.0,
            Direction::Read => self.top_reader.0,
        }
    }

    /// Add another interval of the same name (the per-name speed accumulator).
    pub fn merge(&mut self, other: &NameIo) {
        self.written = self.written.saturating_add(other.written);
        self.read = self.read.saturating_add(other.read);
        if other.top_writer.1 > self.top_writer.1 {
            self.top_writer = other.top_writer;
        }
        if other.top_reader.1 > self.top_reader.1 {
            self.top_reader = other.top_reader;
        }
        self.kernel_thread = other.kernel_thread;
    }
}

/// What one read saw.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct IoRead {
    /// Bytes per name since the previous read: only names with a positive delta. Empty on the
    /// first read, which only sets the baseline.
    pub deltas: BTreeMap<String, NameIo>,
    /// No earlier read: this one is only the baseline.
    pub baseline: bool,
    /// Processes whose `stat` was read.
    pub processes: usize,
    /// Of those, the ones whose `io` was read.
    pub readable: usize,
    /// Of those, the ones whose `io` was refused (`EACCES`): another user's process, whose 0400 `io`
    /// needs `CAP_DAC_READ_SEARCH` to open and `CAP_SYS_PTRACE` to read (kernel:
    /// `ptrace_may_access(PTRACE_MODE_READ_FSCREDS)` in `do_io_accounting`).
    pub denied: usize,
}

/// A process at a read.
#[derive(Clone, Copy, Debug)]
struct Seen {
    start_ticks: u64,
    ppid: u32,
    /// `None`: its `io` could not be read.
    counters: Option<IoCounters>,
}

/// Samples per-process disk I/O between successive reads.
pub struct IoSampler {
    proc_root: PathBuf,
    /// pid → the process at the previous successful read; `None` before the first.
    previous: Option<HashMap<u32, Seen>>,
    /// Reads one `<pid>/io`; injectable so a refused read (`EACCES`) can be tested as root.
    read_io: fn(&Path) -> std::io::Result<String>,
}

fn read_io_file(path: &Path) -> std::io::Result<String> {
    std::fs::read_to_string(path)
}

impl IoSampler {
    pub fn new(proc_root: impl Into<PathBuf>) -> Self {
        Self {
            proc_root: proc_root.into(),
            previous: None,
            read_io: read_io_file,
        }
    }

    #[cfg(test)]
    pub fn with_reader(
        proc_root: impl Into<PathBuf>,
        read_io: fn(&Path) -> std::io::Result<String>,
    ) -> Self {
        Self {
            read_io,
            ..Self::new(proc_root)
        }
    }

    pub fn proc_root(&self) -> &Path {
        &self.proc_root
    }

    /// Read the process table and every readable `io`, and return each name's bytes since the
    /// previous read, or why nothing could be read (only an unlistable `<proc>`; the previous
    /// baseline is then kept, so the next read measures the longer interval).
    ///
    /// A process is `(pid, starttime)`, so a reused pid never inherits its predecessor's counters.
    /// One that started since the previous read (absent from it, or its pid then held by another
    /// process) is credited with its whole counters: the kernel starts a new task's at 0
    /// (`copy_process`), so they all belong to this interval — here this rule differs from top
    /// CPU's, because the daily totals should miss nothing. One whose `io` was unreadable at
    /// either read has no delta. A `<proc>/<pid>` that vanishes while being read is skipped
    /// silently. A vanished process's counters are subtracted from its [`receiver`]'s delta (see
    /// the module documentation).
    pub fn read(&mut self) -> Result<IoRead, String> {
        let processes = read_processes(&self.proc_root)?;
        let mut current: HashMap<u32, Seen> = HashMap::with_capacity(processes.len());
        let (mut readable, mut denied) = (0, 0);
        for stat in &processes {
            let path = self.proc_root.join(stat.pid.to_string()).join("io");
            let counters = match (self.read_io)(&path) {
                Ok(text) => parse_io(&text),
                Err(error) if error.kind() == ErrorKind::PermissionDenied => {
                    denied += 1;
                    None
                }
                // Gone since the listing (normal), or a kernel without task I/O accounting (the
                // caller reports a table with nothing readable).
                Err(_) => None,
            };
            if counters.is_some() {
                readable += 1;
            }
            current.insert(
                stat.pid,
                Seen {
                    start_ticks: stat.start_ticks,
                    ppid: stat.ppid,
                    counters,
                },
            );
        }
        let mut read = IoRead {
            processes: processes.len(),
            readable,
            denied,
            ..IoRead::default()
        };
        let Some(previous) = self.previous.replace(current) else {
            read.baseline = true;
            return Ok(read);
        };
        let current = self.previous.as_ref().unwrap_or(&previous);

        let alive_in_both = |pid: u32, before: &Seen| {
            current
                .get(&pid)
                .filter(|now| now.start_ticks == before.start_ticks)
        };
        let mut deltas: HashMap<u32, IoCounters> = HashMap::new();
        for (&pid, now) in current {
            let Some(now_counters) = now.counters else {
                continue;
            };
            let before_counters = match previous.get(&pid) {
                Some(before) if before.start_ticks == now.start_ticks => match before.counters {
                    Some(counters) => counters,
                    // Unreadable last time: no baseline to measure from.
                    None => continue,
                },
                // Started since the previous read (a new pid, or a reused one): the kernel zeroes
                // a new task's counters, so all of them belong to this interval.
                _ => IoCounters::default(),
            };
            // The counters only grow; a smaller one is not this process's history.
            let (Some(read_bytes), Some(write_bytes)) = (
                now_counters
                    .read_bytes
                    .checked_sub(before_counters.read_bytes),
                now_counters
                    .write_bytes
                    .checked_sub(before_counters.write_bytes),
            ) else {
                continue;
            };
            deltas.insert(
                pid,
                IoCounters {
                    read_bytes,
                    write_bytes,
                },
            );
        }
        // A vanished process's whole counters were added to its reaper's: take back what the
        // previous read already credited to it.
        for (&pid, gone) in &previous {
            if alive_in_both(pid, gone).is_some() {
                continue;
            }
            let Some(counters) = gone.counters else {
                continue;
            };
            let Some(delta) = receiver(pid, &previous, current).and_then(|to| deltas.get_mut(&to))
            else {
                continue;
            };
            delta.read_bytes = delta.read_bytes.saturating_sub(counters.read_bytes);
            delta.write_bytes = delta.write_bytes.saturating_sub(counters.write_bytes);
        }

        for stat in &processes {
            let Some(delta) = deltas.get(&stat.pid) else {
                continue;
            };
            if delta.read_bytes == 0 && delta.write_bytes == 0 {
                continue;
            }
            let kernel_thread = stat.is_kernel_thread();
            let Some(name) = sensor_name(&stat.comm, kernel_thread) else {
                continue;
            };
            read.deltas
                .entry(name)
                .or_insert_with(|| NameIo {
                    kernel_thread,
                    ..NameIo::default()
                })
                .merge(&NameIo {
                    written: delta.write_bytes,
                    read: delta.read_bytes,
                    top_writer: (stat.pid, delta.write_bytes),
                    top_reader: (stat.pid, delta.read_bytes),
                    kernel_thread,
                });
        }
        Ok(read)
    }
}

/// The process a vanished `pid` was reaped by, as far as the two reads can tell: its parent at the
/// previous read when that parent is still alive (same start time) — a shell, `cron`, `systemd`
/// or a container shim reaping its child, the normal case. When the parent vanished too, the
/// child's counters went into the parent's before the parent was reaped in turn (a job tree
/// exiting at once: `cron → sh → tar`), so the walk continues up the previous read's parents to
/// the first one still alive. `None` when the chain leaves the processes the previous read saw.
///
/// An orphan is reaped by its subreaper or by `init`, not by its old grandparent; when its parent
/// died first **and** both vanished within one interval, the walk names the grandparent instead:
/// that one's delta is cut by too much (never below 0) and the real reaper keeps the orphan's
/// whole-life total — a rare case, documented in the README.
fn receiver(pid: u32, previous: &HashMap<u32, Seen>, current: &HashMap<u32, Seen>) -> Option<u32> {
    let mut ppid = previous.get(&pid)?.ppid;
    // Every step moves to a process of the previous read, so the table's size bounds the walk
    // (a pid table cannot hold a cycle, but a torn read could).
    for _ in 0..previous.len() {
        let parent = previous.get(&ppid)?;
        if current
            .get(&ppid)
            .is_some_and(|now| now.start_ticks == parent.start_ticks)
        {
            return Some(ppid);
        }
        ppid = parent.ppid;
    }
    None
}

/// One name's speed over an interval.
#[derive(Clone, Debug, PartialEq)]
pub struct Rate {
    pub name: String,
    pub bytes_per_sec: f64,
    /// The process of the name that moved the most bytes in `direction`.
    pub pid: u32,
    pub kernel_thread: bool,
}

/// The names whose `direction` speed over `seconds` is positive and at least `min_bytes_per_sec`,
/// fastest first, at most `count`, ties by name — the top-CPU rule (`select_top`), on bytes per
/// second. A positive delta is required, so `minBytesPerSec` 0 means "every process that moved
/// a byte", never "every process".
pub fn select_top(
    by_name: &BTreeMap<String, NameIo>,
    direction: Direction,
    seconds: f64,
    min_bytes_per_sec: f64,
    count: usize,
) -> Vec<Rate> {
    if seconds <= 0.0 {
        return Vec::new();
    }
    let mut top: Vec<Rate> = by_name
        .iter()
        .filter(|(_, io)| io.bytes(direction) > 0)
        .map(|(name, io)| Rate {
            name: name.clone(),
            bytes_per_sec: io.bytes(direction) as f64 / seconds,
            pid: io.pid(direction),
            kernel_thread: io.kernel_thread,
        })
        .filter(|rate| rate.bytes_per_sec >= min_bytes_per_sec)
        .collect();
    top.sort_by(|a, b| {
        b.bytes_per_sec
            .total_cmp(&a.bytes_per_sec)
            .then_with(|| a.name.cmp(&b.name))
    });
    top.truncate(count);
    top
}

#[cfg(test)]
pub mod tests {
    use super::*;
    use crate::probe_only::host::tests::FakeTree;
    use crate::probe_only::top_cpu::procfs::tests::stat_line_of_child;

    const PF_KTHREAD: u64 = 0x0020_0000;

    pub fn io_text(read_bytes: u64, write_bytes: u64) -> String {
        format!(
            "rchar: 999999999\nwchar: 888888888\nsyscr: 1\nsyscw: 2\nread_bytes: {read_bytes}\n\
             write_bytes: {write_bytes}\ncancelled_write_bytes: 4096\n"
        )
    }

    /// A process in the fake `/proc`: its `stat` (with the parent) and its `io`.
    pub fn process(
        tree: &FakeTree,
        pid: u32,
        comm: &str,
        ppid: u32,
        start: u64,
        read_bytes: u64,
        write_bytes: u64,
    ) {
        tree.file(
            &format!("{pid}/stat"),
            &stat_line_of_child(pid, comm, ppid, 0, 0, 0, start),
        );
        tree.file(&format!("{pid}/io"), &io_text(read_bytes, write_bytes));
    }

    pub fn gone(tree: &FakeTree, pid: u32) {
        std::fs::remove_dir_all(tree.0.join(pid.to_string())).expect("rm");
    }

    const MB: u64 = 1_048_576;

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
        assert_eq!(parse_io(""), None);
    }

    #[test]
    fn the_first_read_is_a_baseline_then_deltas_are_per_process_and_summed_per_name() {
        let tree = FakeTree::new("topio-delta");
        process(&tree, 100, "postgres", 1, 50, 0, 0);
        process(&tree, 101, "postgres", 100, 51, 10 * MB, 0);
        process(&tree, 200, "rsync", 1, 60, 0, 5 * MB);
        process(&tree, 300, "kworker/u8:2-flush-8:0", 2, 1, 0, 0);
        tree.file(
            "300/stat",
            &stat_line_of_child(300, "kworker/u8:2-flush-8:0", 2, PF_KTHREAD, 0, 0, 1),
        );
        let mut sampler = IoSampler::new(&tree.0);
        let first = sampler.read().expect("read");
        assert!(first.baseline && first.deltas.is_empty());
        assert_eq!((first.processes, first.readable, first.denied), (4, 4, 0));

        process(&tree, 100, "postgres", 1, 50, 2 * MB, 30 * MB);
        process(&tree, 101, "postgres", 100, 51, 10 * MB + 3 * MB, 10 * MB);
        process(&tree, 200, "rsync", 1, 60, 0, 5 * MB); // idle: no entry
        process(&tree, 300, "kworker/u8:2-flush-8:0", 2, 1, 0, MB);
        tree.file(
            "300/stat",
            &stat_line_of_child(300, "kworker/u8:2-flush-8:0", 2, PF_KTHREAD, 0, 0, 1),
        );
        let read = sampler.read().expect("read");
        assert!(!read.baseline);
        assert_eq!(read.deltas.len(), 2, "{:?}", read.deltas);
        let postgres = &read.deltas["postgres"];
        assert_eq!((postgres.written, postgres.read), (40 * MB, 5 * MB));
        assert_eq!(postgres.top_writer, (100, 30 * MB));
        assert_eq!(postgres.top_reader, (101, 3 * MB));
        // A kernel thread is named by its kind, like top CPU.
        assert_eq!(read.deltas["kworker"].written, MB);
        assert!(read.deltas["kworker"].kernel_thread);
    }

    #[test]
    fn a_new_process_counts_from_its_start_and_a_reused_pid_inherits_nothing() {
        let tree = FakeTree::new("topio-reuse");
        process(&tree, 300, "old", 1, 100, 0, 50 * MB);
        let mut sampler = IoSampler::new(&tree.0);
        sampler.read().expect("baseline");

        // pid 300 now belongs to another process: its 60 MB are its own, never a delta of 10 MB
        // against its predecessor. pid 302 started during the interval: all its 7 MB are new.
        process(&tree, 300, "new", 1, 900, 0, 60 * MB);
        process(&tree, 302, "started", 1, 950, 0, 7 * MB);
        let read = sampler.read().expect("read");
        assert_eq!(read.deltas["new"].written, 60 * MB);
        assert_eq!(read.deltas["started"].written, 7 * MB);
        assert!(!read.deltas.contains_key("old"));

        process(&tree, 300, "new", 1, 900, 0, 61 * MB);
        process(&tree, 302, "started", 1, 950, 0, 9 * MB);
        let read = sampler.read().expect("read");
        assert_eq!(read.deltas["new"].written, MB);
        assert_eq!(read.deltas["started"].written, 2 * MB);
    }

    /// The reaper's counter jumps by a reaped child's whole-life total; what the previous read
    /// already credited to the child is taken back, so it is counted once.
    #[test]
    fn a_reaped_child_is_not_counted_twice_under_its_parent() {
        let tree = FakeTree::new("topio-reap");
        process(&tree, 10, "bash", 1, 5, 0, MB);
        process(&tree, 11, "tar", 10, 6, 0, 100 * MB);
        let mut sampler = IoSampler::new(&tree.0);
        sampler.read().expect("baseline");

        // tar wrote 20 MB more, then exited and bash reaped it: bash's counter = its own 1 MB +
        // 2 MB of its own since + tar's 120 MB.
        gone(&tree, 11);
        process(&tree, 10, "bash", 1, 5, 0, MB + 2 * MB + 120 * MB);
        let read = sampler.read().expect("read");
        // bash: its own 2 MB plus tar's last 20 MB (the kernel moved them to bash); never the
        // 100 MB already credited to tar.
        assert_eq!(read.deltas["bash"].written, 22 * MB);
        assert!(!read.deltas.contains_key("tar"));
    }

    /// `cron → sh → tar` all exit within one interval: the counters flowed up the chain into cron.
    #[test]
    fn a_job_tree_exiting_at_once_is_taken_back_from_the_surviving_ancestor() {
        let tree = FakeTree::new("topio-chain");
        process(&tree, 20, "cron", 1, 5, 0, 0);
        process(&tree, 21, "sh", 20, 6, 0, MB);
        process(&tree, 22, "tar", 21, 7, 0, 500 * MB);
        process(&tree, 30, "unrelated", 1, 8, 0, 0);
        let mut sampler = IoSampler::new(&tree.0);
        sampler.read().expect("baseline");

        gone(&tree, 22);
        gone(&tree, 21);
        // cron: sh's final 1 MB + tar's final 510 MB.
        process(&tree, 20, "cron", 1, 5, 0, MB + 510 * MB);
        let read = sampler.read().expect("read");
        assert_eq!(
            read.deltas["cron"].written,
            10 * MB,
            "only tar's last 10 MB"
        );
        assert!(!read.deltas.contains_key("unrelated"));
    }

    /// The correction never drives a delta below zero, and a vanished process whose counters were
    /// unknown (unreadable) takes nothing back.
    #[test]
    fn the_correction_is_bounded_and_needs_known_counters() {
        let tree = FakeTree::new("topio-bounded");
        process(&tree, 40, "parent", 1, 5, 0, 0);
        process(&tree, 41, "child", 40, 6, 0, 300 * MB);
        process(&tree, 43, "other", 1, 8, 0, 0);
        process(&tree, 42, "secret", 43, 7, 0, 0);
        std::fs::remove_file(tree.0.join("42/io")).expect("rm");
        let mut sampler = IoSampler::new(&tree.0);
        sampler.read().expect("baseline");

        // The child was reaped by someone else (reparented): the parent's counter barely moved.
        gone(&tree, 41);
        gone(&tree, 42);
        process(&tree, 40, "parent", 1, 5, 0, 4 * MB);
        process(&tree, 43, "other", 1, 8, 0, 4 * MB);
        let read = sampler.read().expect("read");
        assert!(!read.deltas.contains_key("parent"), "{:?}", read.deltas);
        assert_eq!(
            read.deltas["other"].written,
            4 * MB,
            "nothing known to take back"
        );
    }

    /// What the kernel answers for another user's `io` without the capabilities: an `io` file
    /// holding `EACCES` reads as a refusal (a chmod would not do: the tests may run as root).
    pub fn refusing_reader(path: &Path) -> std::io::Result<String> {
        let text = std::fs::read_to_string(path)?;
        if text == DENIED {
            return Err(std::io::Error::from(ErrorKind::PermissionDenied));
        }
        Ok(text)
    }

    pub const DENIED: &str = "EACCES";

    #[test]
    fn a_refused_io_is_counted_as_denied_and_the_rest_is_still_measured() {
        let tree = FakeTree::new("topio-eacces");
        process(&tree, 1, "init", 0, 1, 0, 0);
        process(&tree, 50, "mine", 1, 5, 0, 0);
        tree.file("1/io", DENIED);
        let mut sampler = IoSampler::with_reader(&tree.0, refusing_reader);
        let first = sampler.read().expect("baseline");
        assert_eq!((first.processes, first.readable, first.denied), (2, 1, 1));

        process(&tree, 50, "mine", 1, 5, 0, 3 * MB);
        let read = sampler.read().expect("read");
        assert_eq!((read.processes, read.readable, read.denied), (2, 1, 1));
        assert_eq!(read.deltas.len(), 1);
        assert_eq!(read.deltas["mine"].written, 3 * MB);

        // The capability granted (the drop-in, a restart): from the next interval on it counts.
        process(&tree, 1, "init", 0, 1, 0, 8 * MB);
        assert!(!sampler.read().expect("read").deltas.contains_key("init"));
        process(&tree, 1, "init", 0, 1, 0, 9 * MB);
        assert_eq!(sampler.read().expect("read").deltas["init"].written, MB);
    }

    #[test]
    fn an_unlistable_proc_is_an_error_and_keeps_the_baseline() {
        let tree = FakeTree::new("topio-unlistable");
        process(&tree, 7, "busy", 1, 1, 0, 0);
        let mut sampler = IoSampler::new(&tree.0);
        sampler.read().expect("baseline");
        assert!(IoSampler::new(tree.0.join("absent")).read().is_err());

        let moved = tree.0.with_extension("moved");
        std::fs::rename(&tree.0, &moved).expect("mv");
        assert!(sampler.read().is_err());
        std::fs::rename(&moved, &tree.0).expect("mv back");
        process(&tree, 7, "busy", 1, 1, 0, 6 * MB);
        assert_eq!(sampler.read().expect("read").deltas["busy"].written, 6 * MB);
    }

    fn io(name: &str, written: u64, read: u64) -> (String, NameIo) {
        (
            name.to_string(),
            NameIo {
                written,
                read,
                top_writer: (1, written),
                top_reader: (1, read),
                kernel_thread: false,
            },
        )
    }

    #[test]
    fn the_top_is_at_least_the_threshold_fastest_first_at_most_count() {
        let by_name: BTreeMap<String, NameIo> = [
            io("a", 60 * MB - 1, 0),
            io("b", 60 * MB, 120 * MB),
            io("c", 600 * MB, 0),
            io("d", 120 * MB, 60 * MB),
            io("e", 120 * MB, 0),
        ]
        .into_iter()
        .collect();
        let names = |top: Vec<Rate>| top.into_iter().map(|r| r.name).collect::<Vec<_>>();
        // 60 s at 1 MiB/s = 60 MiB.
        let min = MB as f64;
        assert_eq!(
            names(select_top(&by_name, Direction::Write, 60.0, min, 10)),
            ["c", "d", "e", "b"]
        );
        assert_eq!(
            names(select_top(&by_name, Direction::Write, 60.0, min, 2)),
            ["c", "d"]
        );
        assert_eq!(
            names(select_top(&by_name, Direction::Read, 60.0, min, 10)),
            ["b", "d"]
        );
        let top = select_top(&by_name, Direction::Write, 60.0, min, 1);
        assert_eq!(top[0].bytes_per_sec, 10.0 * MB as f64);
        // 0 = any positive delta, never a name without one.
        assert_eq!(
            names(select_top(&by_name, Direction::Read, 60.0, 0.0, 10)),
            ["b", "d"]
        );
        assert!(select_top(&by_name, Direction::Write, 0.0, 0.0, 10).is_empty());
    }
}
