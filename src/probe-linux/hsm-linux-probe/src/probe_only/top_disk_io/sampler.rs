//! Per-process disk I/O from the helper's snapshots (#1547): the per-process deltas, the
//! reaped-child correction, the name aggregation and the top-N selection. Pure over
//! [`Snapshot`]s, so every rule is tested on hand-built ones; nothing here reads a file or touches
//! the collector.
//!
//! # The counters
//!
//! `write_bytes` and `read_bytes` of `/proc/<pid>/io`, read by the root helper
//! (`hsm-linux-probe-io`) — the bytes the process caused to be sent to or fetched from the block
//! layer, not `wchar`/`rchar`, which count every `write(2)`/`read(2)` (sockets, pipes, the page
//! cache). `write_bytes` is charged when the process dirties a page, so data flushed later by the
//! kernel still counts for the process that wrote it; `cancelled_write_bytes` (dirty pages
//! truncated before they reached the disk) is **not** subtracted, as `iotop` and `pidstat -d` do
//! not either. For a process the kernel sums every thread, live and exited.
//!
//! # Reaped children
//!
//! The kernel also adds the counters of a child the process has reaped (`wait_task_zombie` adds the
//! child's whole `ioac` to the parent's `signal->ioac`, which `/proc/<pid>/io` includes). So when a
//! process measured so far exits, its reaper's counter jumps by the child's **whole-life** total,
//! which would count everything already credited to the child a second time under the reaper's
//! name (`cron`, `bash`, `systemd`, `containerd-shim`). [`IoSampler::consume`] therefore takes
//! back, from the delta of the receiving process ([`receiver`]), each vanished process's counters
//! at the previous snapshot. What is left is the vanished process's last partial interval, which
//! the kernel moved to the receiver — it is credited there. The helper makes the snapshot
//! race-free (it reads every receiver again after its scan: `hsm_linux_probe_io::scan`), so the
//! jump is always in the same snapshot as the vanishing. A receiver that ignores `SIGCHLD` reaps
//! its children without their counters (`autoreap`): nothing is taken back from it. What a
//! correction cannot take back from the receiver's delta (never below 0) is carried into the next
//! snapshot once, then dropped (logged at debug).

use std::collections::{BTreeMap, HashMap};

use hsm_linux_probe_io::scan::receiver;
use hsm_linux_probe_io::snapshot::{IoCounters, Snapshot};

use crate::probe_only::top_cpu::procfs::sensor_name;

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

/// What one snapshot gave.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct IoRead {
    /// Bytes per name since the previous snapshot: only names with a positive delta. Empty for
    /// the first one, which only sets the baseline.
    pub deltas: BTreeMap<String, NameIo>,
    /// No earlier snapshot: this one is only the baseline.
    pub baseline: bool,
    /// Processes in the snapshot.
    pub processes: usize,
    /// Of those, the ones with readable counters.
    pub readable: usize,
    /// What the caller logs at Debug: counters that went backwards (skipped), corrections that
    /// were skipped or could not be applied — never silent, never worth more than a debug line.
    pub notes: Vec<String>,
}

/// A process at a snapshot.
#[derive(Clone, Copy, Debug)]
struct Seen {
    start_ticks: u64,
    ppid: u32,
    sigchld_ignored: bool,
    /// `None`: its `io` could not be read.
    counters: Option<IoCounters>,
}

fn add(sum: &mut IoCounters, other: IoCounters) {
    sum.read_bytes = sum.read_bytes.saturating_add(other.read_bytes);
    sum.write_bytes = sum.write_bytes.saturating_add(other.write_bytes);
}

/// `now - before` when neither counter went backwards.
fn since(now: IoCounters, before: IoCounters) -> Option<IoCounters> {
    Some(IoCounters {
        read_bytes: now.read_bytes.checked_sub(before.read_bytes)?,
        write_bytes: now.write_bytes.checked_sub(before.write_bytes)?,
    })
}

/// Take `due` out of `delta` as far as it goes (never below 0); returns what is left of `due`.
fn take_back(delta: &mut IoCounters, due: IoCounters) -> IoCounters {
    let left = IoCounters {
        read_bytes: due.read_bytes.saturating_sub(delta.read_bytes),
        write_bytes: due.write_bytes.saturating_sub(delta.write_bytes),
    };
    delta.read_bytes = delta.read_bytes.saturating_sub(due.read_bytes);
    delta.write_bytes = delta.write_bytes.saturating_sub(due.write_bytes);
    left
}

fn is_zero(counters: IoCounters) -> bool {
    counters.read_bytes == 0 && counters.write_bytes == 0
}

/// Turns successive snapshots into per-name deltas.
#[derive(Default)]
pub struct IoSampler {
    /// pid → the process at the previous snapshot; `None` before the first.
    previous: Option<HashMap<u32, Seen>>,
    /// Corrections the previous snapshot could not apply in full, by receiver pid: (its start
    /// time, what is left). Applied once more at the next snapshot, then dropped.
    carried: HashMap<u32, (u64, IoCounters)>,
}

impl IoSampler {
    pub fn new() -> Self {
        Self::default()
    }

    /// Each name's bytes since the previous snapshot.
    ///
    /// A process is `(pid, starttime)`, so a reused pid never inherits its predecessor's counters.
    /// One that started since the previous snapshot (absent from it, or its pid then held by
    /// another process, **and** younger than every process it held) is credited with its whole
    /// counters: the kernel starts a new task's at 0 (`copy_process`), so they all belong to this
    /// interval — here this rule differs from top CPU's, because the daily totals should miss
    /// nothing. One absent but not younger (a scan that missed it) only sets its baseline. One
    /// whose counters are unknown in this snapshot keeps the previous ones (no delta now, the
    /// whole span next time); one unknown in the previous snapshot has no baseline. A vanished
    /// process's counters are taken back from its [`receiver`] (module documentation).
    pub fn consume(&mut self, snapshot: &Snapshot) -> IoRead {
        let previous = self.previous.take();
        let mut read = IoRead {
            processes: snapshot.entries.len(),
            ..IoRead::default()
        };
        let mut current: HashMap<u32, Seen> = HashMap::with_capacity(snapshot.entries.len());
        for entry in &snapshot.entries {
            read.readable += usize::from(entry.counters.is_some());
            let earlier = previous
                .as_ref()
                .and_then(|previous| previous.get(&entry.pid))
                .filter(|seen| seen.start_ticks == entry.start_ticks);
            current.insert(
                entry.pid,
                Seen {
                    start_ticks: entry.start_ticks,
                    ppid: entry.ppid,
                    sigchld_ignored: entry.sigchld_ignored,
                    counters: entry.counters.or(earlier.and_then(|seen| seen.counters)),
                },
            );
        }
        let Some(previous) = previous else {
            read.baseline = true;
            self.previous = Some(current);
            return read;
        };

        let newest_before = previous.values().map(|seen| seen.start_ticks).max();
        let mut deltas: HashMap<u32, IoCounters> = HashMap::new();
        for (&pid, now) in &current {
            let Some(now_counters) = now.counters else {
                continue;
            };
            let before = match previous.get(&pid) {
                Some(before) if before.start_ticks == now.start_ticks => match before.counters {
                    Some(counters) => counters,
                    // Unknown last time: no baseline to measure from.
                    None => continue,
                },
                // Started since the previous snapshot (a new pid, or a reused one): the kernel
                // zeroes a new task's counters, so all of them belong to this interval.
                _ if newest_before.is_none_or(|newest| now.start_ticks > newest) => {
                    IoCounters::default()
                }
                // Older than the previous snapshot's youngest process, yet not in it: that scan
                // missed it, and its counters hold an unknown past. A baseline only.
                _ => continue,
            };
            match since(now_counters, before) {
                Some(delta) => {
                    deltas.insert(pid, delta);
                }
                None => read.notes.push(format!(
                    "the I/O counters of pid {pid} went backwards ({before:?} -> \
                     {now_counters:?}); interval skipped"
                )),
            }
        }

        self.take_back_reaped(&previous, &current, &mut deltas, &mut read.notes);

        for entry in &snapshot.entries {
            let Some(delta) = deltas.get(&entry.pid).filter(|delta| !is_zero(**delta)) else {
                continue;
            };
            let Some(name) = sensor_name(&entry.comm, entry.kernel_thread) else {
                continue;
            };
            read.deltas
                .entry(name)
                .or_insert_with(|| NameIo {
                    kernel_thread: entry.kernel_thread,
                    ..NameIo::default()
                })
                .merge(&NameIo {
                    written: delta.write_bytes,
                    read: delta.read_bytes,
                    top_writer: (entry.pid, delta.write_bytes),
                    top_reader: (entry.pid, delta.read_bytes),
                    kernel_thread: entry.kernel_thread,
                });
        }
        self.previous = Some(current);
        read
    }

    /// Take back from each receiver what the previous snapshot had credited to the processes it
    /// reaped since, plus what the previous snapshot could not take back.
    fn take_back_reaped(
        &mut self,
        previous: &HashMap<u32, Seen>,
        current: &HashMap<u32, Seen>,
        deltas: &mut HashMap<u32, IoCounters>,
        notes: &mut Vec<String>,
    ) {
        let alive = |pid: u32, start: u64| {
            current
                .get(&pid)
                .is_some_and(|now| now.start_ticks == start)
        };
        let lookup = |pid: u32| previous.get(&pid).map(|seen| (seen.start_ticks, seen.ppid));
        let mut due: HashMap<u32, IoCounters> = HashMap::new();
        for (&pid, gone) in previous {
            if alive(pid, gone.start_ticks) {
                continue;
            }
            let (Some(counters), Some(to)) =
                (gone.counters, receiver(pid, lookup, alive, previous.len()))
            else {
                continue;
            };
            add(due.entry(to).or_default(), counters);
        }
        let carried = std::mem::take(&mut self.carried);
        for (to, counters) in due {
            if current.get(&to).is_some_and(|seen| seen.sigchld_ignored) {
                notes.push(format!(
                    "pid {to} ignores SIGCHLD: its children are reaped without their counters, \
                     nothing is taken back from it"
                ));
                continue;
            }
            let Some(delta) = deltas.get_mut(&to) else {
                continue;
            };
            let left = take_back(delta, counters);
            if !is_zero(left) {
                let start = current.get(&to).map_or(0, |seen| seen.start_ticks);
                self.carried.insert(to, (start, left));
            }
        }
        // Last snapshot's remainders: once more, then dropped.
        for (to, (start, counters)) in carried {
            let left = match deltas.get_mut(&to) {
                Some(delta) if alive(to, start) => take_back(delta, counters),
                _ => counters,
            };
            if !is_zero(left) {
                notes.push(format!(
                    "{left:?} of a reaped child's counters could not be taken back from pid {to} \
                     (was it the reaper?); dropped"
                ));
            }
        }
    }
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
    use hsm_linux_probe_io::snapshot::{Entry, Kind};

    pub const MB: u64 = 1_048_576;

    /// A process as the helper writes it: `(pid, comm, ppid, start, read, write)`.
    pub fn entry(pid: u32, comm: &str, ppid: u32, start: u64, read: u64, write: u64) -> Entry {
        Entry {
            pid,
            start_ticks: start,
            ppid,
            kernel_thread: false,
            sigchld_ignored: false,
            counters: Some(IoCounters {
                read_bytes: read,
                write_bytes: write,
            }),
            comm: comm.into(),
        }
    }

    pub fn snapshot(seq: u64, entries: Vec<Entry>) -> Snapshot {
        Snapshot {
            seq,
            mono_ns: seq * 60_000_000_000,
            wall_ms: 0,
            kind: Kind::Regular,
            entries,
        }
    }

    /// Feeds snapshots one after the other.
    struct Feed {
        sampler: IoSampler,
        seq: u64,
    }

    impl Feed {
        fn new(entries: Vec<Entry>) -> Self {
            let mut feed = Self {
                sampler: IoSampler::new(),
                seq: 0,
            };
            assert!(feed.next(entries).baseline);
            feed
        }

        fn next(&mut self, entries: Vec<Entry>) -> IoRead {
            self.seq += 1;
            self.sampler.consume(&snapshot(self.seq, entries))
        }
    }

    #[test]
    fn the_first_snapshot_is_a_baseline_then_deltas_are_per_process_and_summed_per_name() {
        let mut kworker = entry(300, "kworker/u8:2-flush-8:0", 2, 1, 0, 0);
        kworker.kernel_thread = true;
        let mut feed = Feed::new(vec![
            entry(100, "postgres", 1, 50, 0, 0),
            entry(101, "postgres", 100, 51, 10 * MB, 0),
            entry(200, "rsync", 1, 60, 0, 5 * MB),
            kworker.clone(),
        ]);
        kworker.counters = Some(IoCounters {
            read_bytes: 0,
            write_bytes: MB,
        });
        let read = feed.next(vec![
            entry(100, "postgres", 1, 50, 2 * MB, 30 * MB),
            entry(101, "postgres", 100, 51, 13 * MB, 10 * MB),
            entry(200, "rsync", 1, 60, 0, 5 * MB),
            kworker,
        ]);
        assert!(!read.baseline);
        assert_eq!((read.processes, read.readable), (4, 4));
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
        let mut feed = Feed::new(vec![entry(300, "old", 1, 100, 0, 50 * MB)]);
        // pid 300 now belongs to another process: its 60 MB are its own, never a delta of 10 MB
        // against its predecessor. pid 302 started during the interval: all its 7 MB are new.
        let read = feed.next(vec![
            entry(300, "new", 1, 900, 0, 60 * MB),
            entry(302, "started", 1, 950, 0, 7 * MB),
        ]);
        assert_eq!(read.deltas["new"].written, 60 * MB);
        assert_eq!(read.deltas["started"].written, 7 * MB);
        assert!(!read.deltas.contains_key("old"));
        let read = feed.next(vec![
            entry(300, "new", 1, 900, 0, 61 * MB),
            entry(302, "started", 1, 950, 0, 9 * MB),
        ]);
        assert_eq!(read.deltas["new"].written, MB);
        assert_eq!(read.deltas["started"].written, 2 * MB);
    }

    /// The reaper's counter jumps by a reaped child's whole-life total; what the previous
    /// snapshot already credited to the child is taken back, so it is counted once.
    #[test]
    fn a_reaped_child_is_not_counted_twice_under_its_parent() {
        let mut feed = Feed::new(vec![
            entry(10, "bash", 1, 5, 0, MB),
            entry(11, "tar", 10, 6, 0, 100 * MB),
        ]);
        // tar wrote 20 MB more, then bash reaped it: bash = its own 3 MB + tar's final 120 MB.
        let read = feed.next(vec![entry(10, "bash", 1, 5, 0, 123 * MB)]);
        assert_eq!(
            read.deltas["bash"].written,
            22 * MB,
            "own 2 MB + tar's last 20 MB"
        );
        assert!(!read.deltas.contains_key("tar"));
        let next = feed.next(vec![entry(10, "bash", 1, 5, 0, 123 * MB)]);
        assert!(next.deltas.is_empty(), "no late jump");
    }

    /// `cron → sh → tar` all exit within one interval: the counters flowed up into cron.
    #[test]
    fn a_job_tree_exiting_at_once_is_taken_back_from_the_surviving_ancestor() {
        let mut feed = Feed::new(vec![
            entry(20, "cron", 1, 5, 0, 0),
            entry(21, "sh", 20, 6, 0, MB),
            entry(22, "tar", 21, 7, 0, 500 * MB),
            entry(30, "unrelated", 1, 8, 0, 0),
        ]);
        let read = feed.next(vec![
            entry(20, "cron", 1, 5, 0, 511 * MB),
            entry(30, "unrelated", 1, 8, 0, 0),
        ]);
        assert_eq!(
            read.deltas["cron"].written,
            10 * MB,
            "only tar's last 10 MB"
        );
        assert!(!read.deltas.contains_key("unrelated"));
    }

    /// A process whose counters are unknown in a snapshot keeps its previous ones; a vanished
    /// process whose counters were unknown takes nothing back.
    #[test]
    fn unknown_counters_carry_and_take_nothing_back() {
        let mut secret = entry(42, "secret", 43, 7, 0, 0);
        secret.counters = None;
        let mut feed = Feed::new(vec![
            entry(10, "bash", 1, 5, 0, MB),
            entry(11, "tar", 10, 6, 0, 100 * MB),
            entry(43, "other", 1, 8, 0, 0),
            secret,
        ]);
        let mut tar = entry(11, "tar", 10, 6, 0, 0);
        tar.counters = None;
        let read = feed.next(vec![
            entry(10, "bash", 1, 5, 0, 3 * MB),
            tar,
            entry(43, "other", 1, 8, 0, 4 * MB),
        ]);
        assert_eq!(
            read.deltas["bash"].written,
            2 * MB,
            "tar is alive: nothing taken back"
        );
        assert!(!read.deltas.contains_key("tar"));
        assert_eq!(
            read.deltas["other"].written,
            4 * MB,
            "nothing known to take back"
        );
        let read = feed.next(vec![
            entry(10, "bash", 1, 5, 0, 3 * MB),
            entry(11, "tar", 10, 6, 0, 130 * MB),
            entry(43, "other", 1, 8, 0, 4 * MB),
        ]);
        assert_eq!(read.deltas["tar"].written, 30 * MB, "the whole span");
    }

    /// A reaper that ignores SIGCHLD never receives its children's counters (autoreap): nothing
    /// is taken back from its own I/O.
    #[test]
    fn a_reaper_ignoring_sigchld_keeps_its_own_io() {
        let mut feed = Feed::new(vec![
            entry(10, "bash", 1, 5, 0, MB),
            entry(11, "tar", 10, 6, 0, 100 * MB),
        ]);
        let mut bash = entry(10, "bash", 1, 5, 0, 3 * MB);
        bash.sigchld_ignored = true;
        let read = feed.next(vec![bash]);
        assert_eq!(read.deltas["bash"].written, 2 * MB);
        assert!(read.notes.iter().any(|n| n.contains("ignores SIGCHLD")));
    }

    /// What cannot be taken back from the receiver (here the orphan G went to init, not to P) is
    /// tried once more at the next snapshot, then dropped with a note — never carried for ever.
    #[test]
    fn an_unapplied_correction_is_carried_once_then_dropped() {
        let mut feed = Feed::new(vec![
            entry(20, "parent", 1, 5, 0, 0),
            entry(21, "child", 20, 6, 0, MB),
            entry(22, "grandchild", 21, 7, 0, 100 * MB),
        ]);
        let read = feed.next(vec![entry(20, "parent", 1, 5, 0, 5 * MB)]);
        assert!(
            !read.deltas.contains_key("parent"),
            "cut to 0: {:?}",
            read.deltas
        );
        let read = feed.next(vec![entry(20, "parent", 1, 5, 0, 55 * MB)]);
        assert!(!read.deltas.contains_key("parent"), "96 MB more taken back");
        assert!(
            read.notes.iter().any(|n| n.contains("dropped")),
            "{:?}",
            read.notes
        );
        let read = feed.next(vec![entry(20, "parent", 1, 5, 0, 62 * MB)]);
        assert_eq!(read.deltas["parent"].written, 7 * MB);
    }

    /// A process one snapshot missed is not new: only a younger one is credited with its whole
    /// counters; the missed one gets a baseline.
    #[test]
    fn a_process_a_snapshot_missed_is_not_credited_with_its_past() {
        let mut feed = Feed::new(vec![
            entry(30, "old", 1, 100, 0, 0),
            entry(31, "flaky", 1, 200, 0, 500 * MB),
        ]);
        feed.next(vec![
            entry(30, "old", 1, 100, 0, 0),
            entry(32, "young", 1, 300, 0, 0),
        ]);
        let read = feed.next(vec![
            entry(30, "old", 1, 100, 0, 0),
            entry(31, "flaky", 1, 200, 0, 510 * MB),
            entry(32, "young", 1, 300, 0, 0),
            entry(33, "newer", 1, 400, 0, 4 * MB),
        ]);
        assert!(!read.deltas.contains_key("flaky"), "{:?}", read.deltas);
        assert_eq!(read.deltas["newer"].written, 4 * MB);
        let read = feed.next(vec![entry(31, "flaky", 1, 200, 0, 512 * MB)]);
        assert_eq!(read.deltas["flaky"].written, 2 * MB);
    }

    #[test]
    fn a_counter_going_backwards_is_skipped_with_a_note() {
        let mut feed = Feed::new(vec![entry(40, "odd", 1, 5, 0, 9 * MB)]);
        let read = feed.next(vec![entry(40, "odd", 1, 5, 0, 8 * MB)]);
        assert!(read.deltas.is_empty());
        assert!(read.notes.iter().any(|n| n.contains("went backwards")));
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
