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
//!
//! The scan is not atomic, so the correction is made race-free by what the kernel guarantees:
//! `wait_task_zombie` adds the child's counters to the reaper **before** `release_task` removes
//! its pid from `/proc`. So a process missing from the scan has already been added to its reaper,
//! and every receiver of a vanished process has its `io` read **again after the scan** — that
//! value contains the jump, whatever the order of the reads. A process whose `stat` was read but
//! whose `io` is gone by the time it is read (reaped in between) counts as vanished; one whose
//! `io` fails while it is still there keeps its previous counters. A reaper that ignores `SIGCHLD`
//! (`SigIgn` in `<pid>/status`) has its children reaped by the kernel without their counters
//! (`autoreap`), so nothing is taken back from it. What a correction cannot take back from the
//! receiver's delta (never below 0) is carried into the next read once, then dropped (logged).

use std::collections::{BTreeMap, HashMap};
use std::io::ErrorKind;
use std::path::{Path, PathBuf};

use crate::probe_only::top_cpu::procfs::{parse_pid_stat, read_processes, sensor_name, PidStat};

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
    /// What the caller logs at Debug: counters that went backwards (skipped), corrections that
    /// were skipped or could not be applied — never silent, never worth more than a debug line.
    pub notes: Vec<String>,
}

/// A process at a read.
#[derive(Clone, Copy, Debug)]
struct Seen {
    start_ticks: u64,
    ppid: u32,
    /// `None`: its `io` could not be read.
    counters: Option<IoCounters>,
}

impl IoCounters {
    fn add(&mut self, other: IoCounters) {
        self.read_bytes = self.read_bytes.saturating_add(other.read_bytes);
        self.write_bytes = self.write_bytes.saturating_add(other.write_bytes);
    }

    /// `self - other` when neither counter went backwards.
    fn since(self, other: IoCounters) -> Option<IoCounters> {
        Some(IoCounters {
            read_bytes: self.read_bytes.checked_sub(other.read_bytes)?,
            write_bytes: self.write_bytes.checked_sub(other.write_bytes)?,
        })
    }

    /// Take `due` out of `self` as far as it goes (never below 0); returns what is left of `due`.
    fn take_back(&mut self, due: IoCounters) -> IoCounters {
        let left = IoCounters {
            read_bytes: due.read_bytes.saturating_sub(self.read_bytes),
            write_bytes: due.write_bytes.saturating_sub(self.write_bytes),
        };
        self.read_bytes = self.read_bytes.saturating_sub(due.read_bytes);
        self.write_bytes = self.write_bytes.saturating_sub(due.write_bytes);
        left
    }

    fn is_zero(self) -> bool {
        self.read_bytes == 0 && self.write_bytes == 0
    }
}

/// `SIGCHLD` (17) in a `SigIgn:` mask of `<pid>/status`: bit 16.
const SIGCHLD_MASK: u64 = 1 << 16;

/// Whether a `<pid>/status` says the process ignores `SIGCHLD`: then the kernel reaps its children
/// itself (`autoreap`) and their counters are not added to it. `false` when the line is absent or
/// unreadable (the common case: the counters are added).
pub fn ignores_sigchld(status: &str) -> bool {
    status
        .lines()
        .find_map(|line| line.strip_prefix("SigIgn:"))
        .and_then(|mask| u64::from_str_radix(mask.trim(), 16).ok())
        .is_some_and(|mask| mask & SIGCHLD_MASK != 0)
}

/// Samples per-process disk I/O between successive reads.
pub struct IoSampler {
    proc_root: PathBuf,
    /// pid → the process at the previous successful read; `None` before the first.
    previous: Option<HashMap<u32, Seen>>,
    /// Corrections the previous read could not apply in full, by receiver pid: (its start time,
    /// what is left). Applied once more at the next read, then dropped.
    carried: HashMap<u32, (u64, IoCounters)>,
    /// Reads one `<pid>/io`; injectable so a refused read (`EACCES`) and a reap in the middle of a
    /// scan can be tested.
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
            carried: HashMap::new(),
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
    /// process, **and** younger than every process that read saw) is credited with its whole
    /// counters: the kernel starts a new task's at 0 (`copy_process`), so they all belong to this
    /// interval — here this rule differs from top CPU's, because the daily totals should miss
    /// nothing. A process absent from the previous read but not younger than it (a read that missed
    /// it) only sets its baseline. One whose `io` was unreadable at either read has no delta. A
    /// vanished process's counters are taken back from its [`receiver`] (module documentation).
    pub fn read(&mut self) -> Result<IoRead, String> {
        let mut processes = read_processes(&self.proc_root)?;
        // Ascending pids, like /proc itself, so a scan reads in a stable order.
        processes.sort_by_key(|stat| stat.pid);
        let previous = self.previous.take();
        let mut read = IoRead::default();
        let mut current: HashMap<u32, Seen> = HashMap::with_capacity(processes.len());
        let mut alive: Vec<&PidStat> = Vec::with_capacity(processes.len());
        for stat in &processes {
            let earlier = previous
                .as_ref()
                .and_then(|previous| previous.get(&stat.pid))
                .filter(|seen| seen.start_ticks == stat.start_ticks);
            let counters = match self.read_counters(stat.pid) {
                Ok(counters) => {
                    read.readable += usize::from(counters.is_some());
                    counters
                }
                Err(error) if error.kind() == ErrorKind::PermissionDenied => {
                    read.denied += 1;
                    None
                }
                // ENOENT/ESRCH: reaped since its stat was read — then it has already been added to
                // its reaper and is treated as vanished — or a kernel without task I/O accounting
                // (the caller reports a table with nothing readable). Still there: it keeps what
                // the previous read knew, so its next delta spans both intervals.
                Err(_) => {
                    if !self.is_there(stat) {
                        continue;
                    }
                    earlier.and_then(|seen| seen.counters)
                }
            };
            current.insert(
                stat.pid,
                Seen {
                    start_ticks: stat.start_ticks,
                    ppid: stat.ppid,
                    counters,
                },
            );
            alive.push(stat);
        }
        read.processes = alive.len();
        let Some(previous) = previous else {
            read.baseline = true;
            self.previous = Some(current);
            return Ok(read);
        };

        let newest_before = previous.values().map(|seen| seen.start_ticks).max();
        let mut deltas: HashMap<u32, IoCounters> = HashMap::new();
        for (&pid, now) in &current {
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
                _ if newest_before.is_none_or(|newest| now.start_ticks > newest) => {
                    IoCounters::default()
                }
                // Older than the previous read's youngest process, yet not in it: that read
                // missed it, and its counters hold an unknown past. A baseline only.
                _ => continue,
            };
            match now_counters.since(before_counters) {
                Some(delta) => {
                    deltas.insert(pid, delta);
                }
                None => read.notes.push(format!(
                    "the I/O counters of pid {pid} went backwards ({before_counters:?} -> \
                     {now_counters:?}); interval skipped"
                )),
            }
        }

        self.take_back_reaped(&previous, &mut current, &mut deltas, &mut read.notes);

        for stat in alive {
            let Some(delta) = deltas.get(&stat.pid).filter(|delta| !delta.is_zero()) else {
                continue;
            };
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
        self.previous = Some(current);
        Ok(read)
    }

    /// Take back from each receiver what the previous read had credited to the processes it
    /// reaped since (module documentation), plus what the previous read could not take back.
    fn take_back_reaped(
        &mut self,
        previous: &HashMap<u32, Seen>,
        current: &mut HashMap<u32, Seen>,
        deltas: &mut HashMap<u32, IoCounters>,
        notes: &mut Vec<String>,
    ) {
        let alive_in_both = |current: &HashMap<u32, Seen>, pid: u32, start: u64| {
            current
                .get(&pid)
                .is_some_and(|now| now.start_ticks == start)
        };
        let mut due: HashMap<u32, IoCounters> = HashMap::new();
        for (&pid, gone) in previous {
            if alive_in_both(current, pid, gone.start_ticks) {
                continue;
            }
            let (Some(counters), Some(to)) = (gone.counters, receiver(pid, previous, current))
            else {
                continue;
            };
            due.entry(to).or_default().add(counters);
        }
        let mut carried = std::mem::take(&mut self.carried);
        for (to, counters) in due {
            if self.reaps_without_counters(to) {
                notes.push(format!(
                    "pid {to} ignores SIGCHLD: its children are reaped without their counters, \
                     nothing is taken back from it"
                ));
                continue;
            }
            // Read again now: every vanished child is already in this value (the kernel adds it
            // before it removes the pid), whatever the order of the scan's reads.
            if let (Some(before), Ok(Some(after))) = (
                previous.get(&to).and_then(|seen| seen.counters),
                self.read_counters(to),
            ) {
                if let (Some(seen), Some(delta)) = (current.get_mut(&to), after.since(before)) {
                    seen.counters = Some(after);
                    deltas.insert(to, delta);
                }
            }
            let Some(delta) = deltas.get_mut(&to) else {
                continue;
            };
            let left = delta.take_back(counters);
            if !left.is_zero() {
                let start = current.get(&to).map_or(0, |seen| seen.start_ticks);
                self.carried.insert(to, (start, left));
            }
        }
        // Last read's remainders: once more, then dropped.
        for (to, (start, counters)) in carried.drain() {
            let left = match deltas.get_mut(&to) {
                Some(delta) if alive_in_both(current, to, start) => delta.take_back(counters),
                _ => counters,
            };
            if !left.is_zero() {
                notes.push(format!(
                    "{left:?} of a reaped child's counters could not be taken back from pid {to} \
                     (was it the reaper?); dropped"
                ));
            }
        }
    }

    /// `<pid>/io`: `Ok(None)` when it is there but unparsable.
    fn read_counters(&self, pid: u32) -> std::io::Result<Option<IoCounters>> {
        (self.read_io)(&self.proc_root.join(pid.to_string()).join("io")).map(|text| parse_io(&text))
    }

    /// Whether the process whose `stat` was read is still there (the same start time).
    fn is_there(&self, stat: &PidStat) -> bool {
        std::fs::read(self.proc_root.join(stat.pid.to_string()).join("stat"))
            .ok()
            .and_then(|bytes| parse_pid_stat(&String::from_utf8_lossy(&bytes)))
            .is_some_and(|now| now.start_ticks == stat.start_ticks)
    }

    fn reaps_without_counters(&self, pid: u32) -> bool {
        std::fs::read_to_string(self.proc_root.join(pid.to_string()).join("status"))
            .is_ok_and(|status| ignores_sigchld(&status))
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
/// that one's delta is cut by too much (never below 0, the rest carried into one more read) and the
/// real reaper keeps the orphan's whole-life total — a rare case, documented in the README.
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

    /// The kernel's answers, scripted by what an `io` file holds (a chmod would not do: the tests
    /// may run as root):
    ///
    /// * `EACCES` — another user's `io` without the capabilities: refused;
    /// * `ENOENT` — a read that fails while the process is still there;
    /// * `REAP <reaper> <read> <write>` — the process is reaped **between its `stat` and `io`
    ///   reads**: the reaper's counters become `<read> <write>`, the pid disappears, `ENOENT`;
    /// * `THEN-REAP <child> <read> <write>` on its first line — this (the reaper's) `io` is read
    ///   first, then `<child>` is reaped: the rest of the file is returned and the reaper's
    ///   counters become `<read> <write>`, the child's pid disappears.
    pub fn refusing_reader(path: &Path) -> std::io::Result<String> {
        let text = std::fs::read_to_string(path)?;
        let dir = path.parent().expect("pid dir");
        let proc_root = dir.parent().expect("proc root");
        let numbers = |rest: &str| -> Vec<u64> {
            rest.split_whitespace()
                .map(|n| n.parse().expect("number"))
                .collect()
        };
        if text == DENIED {
            return Err(std::io::Error::from(ErrorKind::PermissionDenied));
        }
        if text == VANISHING {
            return Err(std::io::Error::from(ErrorKind::NotFound));
        }
        if let Some(rest) = text.strip_prefix("REAP ") {
            let n = numbers(rest);
            std::fs::write(
                proc_root.join(n[0].to_string()).join("io"),
                io_text(n[1], n[2]),
            )?;
            std::fs::remove_dir_all(dir)?;
            return Err(std::io::Error::from(ErrorKind::NotFound));
        }
        if let Some(rest) = text.strip_prefix("THEN-REAP ") {
            let (first, io) = rest.split_once('\n').expect("two parts");
            let n = numbers(first);
            std::fs::remove_dir_all(proc_root.join(n[0].to_string()))?;
            std::fs::write(path, io_text(n[1], n[2]))?;
            return Ok(io.to_string());
        }
        Ok(text)
    }

    pub const DENIED: &str = "EACCES";
    const VANISHING: &str = "ENOENT";

    fn sampler(tree: &FakeTree) -> IoSampler {
        IoSampler::with_reader(&tree.0, refusing_reader)
    }

    /// The scenario of the reap tests: bash (10) and its child tar (11) at the baseline, tar at
    /// 100 MB written. Since then bash wrote 2 MB itself and tar 20 MB more before it exited.
    fn bash_and_tar(tag: &str, bash: u32, tar: u32) -> (FakeTree, IoSampler) {
        let tree = FakeTree::new(tag);
        process(&tree, bash, "bash", 1, 5, 0, MB);
        process(&tree, tar, "tar", bash, 6, 0, 100 * MB);
        let mut sampler = sampler(&tree);
        sampler.read().expect("baseline");
        (tree, sampler)
    }

    /// bash's counters after the reap: its own 3 MB plus tar's final 120 MB.
    const AFTER_REAP: u64 = 3 * MB + 120 * MB;

    /// bash is credited with its own 2 MB plus tar's last 20 MB, once; the next read sees no jump.
    fn assert_counted_once(tree: &FakeTree, sampler: &mut IoSampler) {
        let read = sampler.read().expect("read");
        assert_eq!(read.deltas["bash"].written, 22 * MB, "{:?}", read.deltas);
        assert!(!read.deltas.contains_key("tar"));
        assert!(!tree.0.join("11").exists(), "tar was reaped");
        let next = sampler.read().expect("read");
        assert!(next.deltas.is_empty(), "no late jump: {:?}", next.deltas);
    }

    /// The parent's `io` is read, then the child is reaped before its own `io` is read: the
    /// correction must not meet the parent's pre-reap value and the jump must not come later.
    #[test]
    fn a_child_reaped_right_after_its_parents_read_is_taken_back_once() {
        let (tree, mut sampler) = bash_and_tar("topio-race-a", 10, 11);
        tree.file(
            "10/io",
            &format!("THEN-REAP 11 0 {AFTER_REAP}\n{}", io_text(0, 3 * MB)),
        );
        assert_counted_once(&tree, &mut sampler);
    }

    /// The child's `stat` is read, then it is reaped before its `io` is read: it counts as
    /// vanished in this very read, not as a live process with unknown counters.
    #[test]
    fn a_child_reaped_between_its_stat_and_io_reads_counts_as_vanished() {
        let (tree, mut sampler) = bash_and_tar("topio-race-b", 10, 11);
        process(&tree, 10, "bash", 1, 5, 0, 3 * MB);
        tree.file("11/io", &format!("REAP 10 0 {AFTER_REAP}"));
        assert_counted_once(&tree, &mut sampler);
    }

    /// After a pid wrap the parent has the higher pid, so the scan reads the child first.
    #[test]
    fn a_parent_with_a_higher_pid_than_its_child_is_corrected_in_the_same_read() {
        let (tree, mut sampler) = bash_and_tar("topio-race-c", 900, 11);
        process(&tree, 900, "bash", 1, 5, 0, 3 * MB);
        tree.file("11/io", &format!("REAP 900 0 {AFTER_REAP}"));
        assert_counted_once(&tree, &mut sampler);
    }

    /// An `io` read that fails while the process is still there keeps its previous counters: no
    /// delta now, the whole span at the next read — and no correction for a live process.
    #[test]
    fn an_io_read_failing_for_a_live_process_keeps_its_counters() {
        let (tree, mut sampler) = bash_and_tar("topio-enoent", 10, 11);
        process(&tree, 10, "bash", 1, 5, 0, 3 * MB);
        tree.file("11/io", VANISHING);
        let read = sampler.read().expect("read");
        assert_eq!(read.deltas["bash"].written, 2 * MB, "nothing taken back");
        assert!(!read.deltas.contains_key("tar"));
        process(&tree, 11, "tar", 10, 6, 0, 130 * MB);
        assert_eq!(sampler.read().expect("read").deltas["tar"].written, 30 * MB);
    }

    /// A reaper that ignores SIGCHLD never receives its children's counters (autoreap): nothing
    /// is taken back from its own I/O.
    #[test]
    fn a_reaper_ignoring_sigchld_keeps_its_own_io() {
        let (tree, mut sampler) = bash_and_tar("topio-sigign", 10, 11);
        tree.file(
            "10/status",
            "Name:\tbash\nSigBlk:\t0000000000000000\nSigIgn:\t0000000000010004\nSigCgt:\t0\n",
        );
        gone(&tree, 11);
        process(&tree, 10, "bash", 1, 5, 0, 3 * MB);
        let read = sampler.read().expect("read");
        assert_eq!(read.deltas["bash"].written, 2 * MB);
        assert!(read.notes.iter().any(|n| n.contains("ignores SIGCHLD")));
    }

    #[test]
    fn sigign_is_read_from_the_status_mask() {
        assert!(ignores_sigchld("SigIgn:\t0000000000010000\n"));
        // kthreadd ignores every signal: its kernel threads are reaped without their counters.
        assert!(ignores_sigchld(
            "Name:\tkthreadd\nSigIgn:\tffffffffffffffff\n"
        ));
        assert!(!ignores_sigchld("SigIgn:\t0000000000001000\n"));
        assert!(!ignores_sigchld("SigIgn:\t0000000000000000\n"));
        assert!(!ignores_sigchld("Name:\tbash\n"));
        assert!(!ignores_sigchld("SigIgn:\tzz\n"));
    }

    /// What cannot be taken back from the receiver (here the orphan G went to init, not to P) is
    /// tried once more at the next read, then dropped with a note — never carried for ever.
    #[test]
    fn an_unapplied_correction_is_carried_once_then_dropped() {
        let tree = FakeTree::new("topio-carry");
        process(&tree, 20, "parent", 1, 5, 0, 0);
        process(&tree, 21, "child", 20, 6, 0, MB);
        process(&tree, 22, "grandchild", 21, 7, 0, 100 * MB);
        let mut sampler = sampler(&tree);
        sampler.read().expect("baseline");

        gone(&tree, 21);
        gone(&tree, 22);
        process(&tree, 20, "parent", 1, 5, 0, 5 * MB);
        let read = sampler.read().expect("read");
        assert!(
            !read.deltas.contains_key("parent"),
            "cut to 0: {:?}",
            read.deltas
        );

        process(&tree, 20, "parent", 1, 5, 0, 55 * MB);
        let read = sampler.read().expect("read");
        assert!(!read.deltas.contains_key("parent"), "96 MB more taken back");
        assert!(
            read.notes.iter().any(|n| n.contains("dropped")),
            "{:?}",
            read.notes
        );

        process(&tree, 20, "parent", 1, 5, 0, 62 * MB);
        assert_eq!(
            sampler.read().expect("read").deltas["parent"].written,
            7 * MB
        );
    }

    /// A process one read missed is not new: only a younger one is credited with its whole
    /// counters; the missed one gets a baseline.
    #[test]
    fn a_process_a_read_missed_is_not_credited_with_its_past() {
        let tree = FakeTree::new("topio-missed");
        process(&tree, 30, "old", 1, 100, 0, 0);
        process(&tree, 31, "flaky", 1, 200, 0, 500 * MB);
        let mut sampler = sampler(&tree);
        sampler.read().expect("baseline");

        // flaky's stat is missing from this read; a younger process exists.
        let moved = tree.0.join("31-away");
        std::fs::rename(tree.0.join("31"), &moved).expect("mv");
        process(&tree, 32, "young", 1, 300, 0, 0);
        sampler.read().expect("read");

        std::fs::rename(&moved, tree.0.join("31")).expect("mv back");
        process(&tree, 31, "flaky", 1, 200, 0, 510 * MB);
        process(&tree, 33, "newer", 1, 400, 0, 4 * MB);
        let read = sampler.read().expect("read");
        assert!(!read.deltas.contains_key("flaky"), "{:?}", read.deltas);
        assert_eq!(read.deltas["newer"].written, 4 * MB);

        process(&tree, 31, "flaky", 1, 200, 0, 512 * MB);
        assert_eq!(
            sampler.read().expect("read").deltas["flaky"].written,
            2 * MB
        );
    }

    #[test]
    fn a_counter_going_backwards_is_skipped_with_a_note() {
        let tree = FakeTree::new("topio-backwards");
        process(&tree, 40, "odd", 1, 5, 0, 9 * MB);
        let mut sampler = sampler(&tree);
        sampler.read().expect("baseline");
        process(&tree, 40, "odd", 1, 5, 0, 8 * MB);
        let read = sampler.read().expect("read");
        assert!(read.deltas.is_empty());
        assert!(read.notes.iter().any(|n| n.contains("went backwards")));
    }

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
