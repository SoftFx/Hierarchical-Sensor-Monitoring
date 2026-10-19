//! Per-process disk I/O from the helper's snapshots (#1547): the per-process deltas, the
//! reaped-child correction and attribution, the name aggregation and the top-N selection. Pure over
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
//! at the previous snapshot. The helper makes the snapshot race-free (it reads every receiver again
//! after its scan: `hsm_linux_probe_io::scan`), so the jump is always in the same snapshot as the
//! vanishing. A receiver that ignores `SIGCHLD` reaps its children without their counters
//! (`autoreap`): nothing is taken back from it. What a correction cannot take back from the
//! receiver's delta (never below 0) is carried into the next snapshot once, then dropped (logged
//! at debug).
//!
//! # Whose bytes a reap moves (#1572)
//!
//! What is left of the jump is still the exited processes' I/O — their last partial interval, and
//! the whole life of any child too short-lived to be read — and crediting it to the reaper made a
//! night of backup jobs read as `Top disk writers/systemd`. A version-2 snapshot carries, per
//! process, its **reaped part** (the counters exited threads and reaped children added: the whole
//! minus the live threads') and `cminflt`, which grows with every reap and never when a thread
//! exits. So a process's delta is split in two:
//!
//! * **its reaped children's** — how much its reaped part grew, but only over an interval in which
//!   its `cminflt` grew too (otherwise exited threads', i.e. its own), and never more than the
//!   delta. The corrections above are taken back from this part first (then from its own). What
//!   is left goes to the **names of the processes a snapshot had seen that it reaped** (each
//!   vanished process whose [`receiver`] it is: a whole job tree that exits at once lands on the
//!   tree's names), shared in proportion to what each moved in its last measured interval (when
//!   none moved anything then — seen at one snapshot only —, to what each had done itself over its
//!   life; equally when that is nothing too); when it reaped none that was seen — only children
//!   too short-lived for any scan — to [`children_of`] its name (`children of systemd`), never to
//!   its own name;
//! * **its own** — the rest, under its own name as before.
//!
//! A version-1 snapshot, a kernel thread, or a process whose threads could not be read has no
//! reaped part: all its delta is its own, as before #1572. Shares of vanished processes and
//! `children of …` have no process to name in a description ([`NO_PROCESS`]).

use std::collections::{BTreeMap, HashMap};

use hsm_linux_probe_io::procstat::original_comm;
use hsm_linux_probe_io::scan::receiver;
use hsm_linux_probe_io::snapshot::{IoCounters, Snapshot};

use crate::probe_only::top_cpu::procfs::sensor_name;

/// The representative "pid" of bytes no live process stands for — a vanished process's share, a
/// reaper's `children of …`: a sensor first posted for them gets no path line.
pub const NO_PROCESS: u32 = 0;

/// The name the bytes of children no snapshot saw are credited under: `children of <reaper>`
/// (#1572). Only characters the server's template wildcard matches (no parentheses), so the name
/// stays one sensor segment like every other.
pub fn children_of(reaper: &str) -> String {
    format!("children of {reaper}")
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
    /// executable a writers' sensor description names. A live process is preferred to
    /// [`NO_PROCESS`] (none), whatever the bytes ([`NameIo::merge`]).
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
        if takes_over(self.top_writer, other.top_writer) {
            self.top_writer = other.top_writer;
        }
        if takes_over(self.top_reader, other.top_reader) {
            self.top_reader = other.top_reader;
        }
        self.kernel_thread = other.kernel_thread;
    }
}

/// Whether `other` replaces `current` as a name's representative `(pid, bytes)`: a live process
/// always beats [`NO_PROCESS`] (a vanished process's share, `children of …`), whatever the bytes,
/// so a sensor created for the name gets a path line whenever a live process stands for it;
/// otherwise the one that moved more.
fn takes_over(current: (u32, u64), other: (u32, u64)) -> bool {
    match (current.0 == NO_PROCESS, other.0 == NO_PROCESS) {
        (true, false) => true,
        (false, true) => false,
        _ => other.1 > current.1,
    }
}

/// What one snapshot gave.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct IoRead {
    /// Bytes per name since the previous snapshot: only names with a positive delta. Empty for
    /// the first one, which only sets the baseline.
    pub deltas: BTreeMap<String, NameIo>,
    /// Names credited here that the helper extended from a 15-byte `comm` (#1573), each with the
    /// name it replaces — for the caller's one-time "renamed" line.
    pub renamed: BTreeMap<String, String>,
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
#[derive(Clone, Debug)]
struct Seen {
    start_ticks: u64,
    ppid: u32,
    sigchld_ignored: bool,
    /// `None`: its `io` could not be read.
    counters: Option<IoCounters>,
    /// The part of `counters` from exited threads and reaped children; `None`: unknown.
    reaped: Option<IoCounters>,
    /// The reaped children's minor faults: growing, it says the process reaped a child.
    cminflt: u64,
    /// Its sensor name (`None`: nothing left after normalization).
    name: Option<String>,
    /// The name its 15-byte `comm` gave before the helper extended it (#1573).
    renamed_from: Option<String>,
    /// What it moved over its last measured interval (own + reaped children's): its weight in a
    /// share of its reaper's bytes once it has vanished.
    last: IoCounters,
}

/// A vanished process whose bytes went to a receiver: who gets a share of them.
#[derive(Clone, Debug)]
struct Heir {
    pid: u32,
    name: Option<String>,
    renamed_from: Option<String>,
    /// What it moved over its last measured interval.
    last: IoCounters,
    /// What it had done itself over its life at the previous snapshot (its counters without the
    /// reaped part): the weight when no heir moved anything in its last interval.
    lifetime: IoCounters,
}

/// The heirs' weights in one direction: what each moved in its last measured interval; when that
/// is nothing for all of them (seen at one snapshot only), what each did itself over its life.
fn weights(heirs: &[Heir], pick: fn(&IoCounters) -> u64) -> Vec<u64> {
    let last: Vec<u64> = heirs.iter().map(|heir| pick(&heir.last)).collect();
    if last.iter().any(|&weight| weight > 0) {
        last
    } else {
        heirs.iter().map(|heir| pick(&heir.lifetime)).collect()
    }
}

/// A process's delta: its own bytes and those its reaped children added.
#[derive(Clone, Copy, Debug, Default)]
struct Split {
    own: IoCounters,
    children: IoCounters,
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

/// [`take_back`] from the reaped children's part first, then from the process's own.
fn take_back_split(split: &mut Split, due: IoCounters) -> IoCounters {
    let left = take_back(&mut split.children, due);
    take_back(&mut split.own, left)
}

fn is_zero(counters: IoCounters) -> bool {
    counters.read_bytes == 0 && counters.write_bytes == 0
}

/// Split a process's `delta` (module documentation): its reaped part's growth is its children's
/// when its `cminflt` grew too, never more than `delta`; the rest is its own.
fn split(
    delta: IoCounters,
    now: &Seen,
    before_reaped: Option<IoCounters>,
    before_cminflt: u64,
) -> Split {
    let children = match (now.reaped, before_reaped) {
        (Some(now_reaped), Some(before_reaped)) if now.cminflt > before_cminflt => IoCounters {
            read_bytes: now_reaped
                .read_bytes
                .saturating_sub(before_reaped.read_bytes)
                .min(delta.read_bytes),
            write_bytes: now_reaped
                .write_bytes
                .saturating_sub(before_reaped.write_bytes)
                .min(delta.write_bytes),
        },
        _ => IoCounters::default(),
    };
    Split {
        own: IoCounters {
            read_bytes: delta.read_bytes - children.read_bytes,
            write_bytes: delta.write_bytes - children.write_bytes,
        },
        children,
    }
}

/// `total` shared in proportion to `weights` (equally when they are all 0), in whole bytes that add
/// up to `total`: the rounding remainder goes to the largest weight (the first on a tie).
fn shares(total: u64, weights: &[u64]) -> Vec<u64> {
    let sum: u128 = weights.iter().map(|&weight| u128::from(weight)).sum();
    let count = weights.len().max(1) as u128;
    let mut out: Vec<u64> = weights
        .iter()
        .map(|&weight| {
            // Two u64 multiplied always fit in a u128; all weights 0 means equal shares.
            let part = (u128::from(total) * u128::from(weight))
                .checked_div(sum)
                .unwrap_or(u128::from(total) / count);
            u64::try_from(part).unwrap_or(u64::MAX)
        })
        .collect();
    let given: u64 = out.iter().fold(0u64, |sum, part| sum.saturating_add(*part));
    if let Some((largest, _)) = weights
        .iter()
        .enumerate()
        .max_by(|a, b| a.1.cmp(b.1).then(b.0.cmp(&a.0)))
    {
        out[largest] = out[largest].saturating_add(total.saturating_sub(given));
    }
    out
}

/// Add `bytes` to `name` in `deltas`; whether anything was added.
fn credit(
    deltas: &mut BTreeMap<String, NameIo>,
    name: &str,
    kernel_thread: bool,
    pid: u32,
    bytes: IoCounters,
) -> bool {
    if is_zero(bytes) {
        return false;
    }
    deltas
        .entry(name.to_string())
        .or_insert_with(|| NameIo {
            kernel_thread,
            ..NameIo::default()
        })
        .merge(&NameIo {
            written: bytes.write_bytes,
            read: bytes.read_bytes,
            top_writer: (pid, bytes.write_bytes),
            top_reader: (pid, bytes.read_bytes),
            kernel_thread,
        });
    true
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
    /// process's counters are taken back from its [`receiver`], and what its receiver's reaped
    /// part gained is credited by the reaped children's names (module documentation).
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
            // Unknown counters keep the previous ones, with the reaped part and the fault count
            // they were read with, so the next delta spans both intervals consistently.
            let (counters, reaped, cminflt) = match (entry.counters, earlier) {
                (Some(counters), _) => (Some(counters), entry.reaped, entry.cminflt),
                (None, Some(seen)) => (seen.counters, seen.reaped, seen.cminflt),
                (None, None) => (None, None, entry.cminflt),
            };
            let name = sensor_name(&entry.name, entry.kernel_thread);
            let renamed_from = (!entry.kernel_thread)
                .then(|| original_comm(&entry.name))
                .flatten()
                .and_then(|comm| sensor_name(&comm, false))
                .filter(|old| name.as_ref() != Some(old));
            current.insert(
                entry.pid,
                Seen {
                    start_ticks: entry.start_ticks,
                    ppid: entry.ppid,
                    sigchld_ignored: entry.sigchld_ignored,
                    counters,
                    reaped,
                    cminflt,
                    name,
                    renamed_from,
                    last: IoCounters::default(),
                },
            );
        }
        let Some(previous) = previous else {
            read.baseline = true;
            self.previous = Some(current);
            return read;
        };

        let newest_before = previous.values().map(|seen| seen.start_ticks).max();
        let mut deltas: HashMap<u32, Split> = HashMap::new();
        for (&pid, now) in &current {
            let Some(now_counters) = now.counters else {
                continue;
            };
            let (before, before_reaped, before_cminflt) = match previous.get(&pid) {
                Some(before) if before.start_ticks == now.start_ticks => match before.counters {
                    Some(counters) => (counters, before.reaped, before.cminflt),
                    // Unknown last time: no baseline to measure from.
                    None => continue,
                },
                // Started since the previous snapshot (a new pid, or a reused one): the kernel
                // zeroes a new task's counters and fault counts, so all of them belong to this
                // interval.
                _ if newest_before.is_none_or(|newest| now.start_ticks > newest) => {
                    (IoCounters::default(), Some(IoCounters::default()), 0)
                }
                // Older than the previous snapshot's youngest process, yet not in it: that scan
                // missed it, and its counters hold an unknown past. A baseline only.
                _ => continue,
            };
            match since(now_counters, before) {
                Some(delta) => {
                    deltas.insert(pid, split(delta, now, before_reaped, before_cminflt));
                }
                None => read.notes.push(format!(
                    "the I/O counters of pid {pid} went backwards ({before:?} -> \
                     {now_counters:?}); interval skipped"
                )),
            }
        }

        let heirs = self.take_back_reaped(&previous, &current, &mut deltas, &mut read.notes);

        for entry in &snapshot.entries {
            let Some(split) = deltas.get(&entry.pid).copied() else {
                continue;
            };
            let Some(now) = current.get_mut(&entry.pid) else {
                continue;
            };
            now.last = split.own;
            add(&mut now.last, split.children);
            let Some(name) = now.name.clone() else {
                continue;
            };
            let mut credited = credit(
                &mut read.deltas,
                &name,
                entry.kernel_thread,
                entry.pid,
                split.own,
            );
            if !is_zero(split.children) {
                match heirs.get(&entry.pid).filter(|heirs| !heirs.is_empty()) {
                    Some(heirs) => {
                        let written = shares(
                            split.children.write_bytes,
                            &weights(heirs, |c| c.write_bytes),
                        );
                        let fetched =
                            shares(split.children.read_bytes, &weights(heirs, |c| c.read_bytes));
                        for ((heir, write_bytes), read_bytes) in
                            heirs.iter().zip(written).zip(fetched)
                        {
                            let bytes = IoCounters {
                                read_bytes,
                                write_bytes,
                            };
                            match &heir.name {
                                Some(heir_name) => {
                                    if credit(&mut read.deltas, heir_name, false, NO_PROCESS, bytes)
                                    {
                                        if let Some(old) = &heir.renamed_from {
                                            read.renamed.insert(heir_name.clone(), old.clone());
                                        }
                                    }
                                }
                                None => {
                                    credited |= credit(
                                        &mut read.deltas,
                                        &children_of(&name),
                                        false,
                                        NO_PROCESS,
                                        bytes,
                                    )
                                }
                            }
                        }
                    }
                    None => {
                        credited |= credit(
                            &mut read.deltas,
                            &children_of(&name),
                            false,
                            NO_PROCESS,
                            split.children,
                        )
                    }
                }
            }
            if credited {
                if let Some(old) = &now.renamed_from {
                    read.renamed.insert(name, old.clone());
                }
            }
        }
        self.previous = Some(current);
        read
    }

    /// Take back from each receiver what the previous snapshot had credited to the processes it
    /// reaped since, plus what the previous snapshot could not take back. Returns, per receiver,
    /// the vanished processes whose bytes it received, by pid.
    fn take_back_reaped(
        &mut self,
        previous: &HashMap<u32, Seen>,
        current: &HashMap<u32, Seen>,
        deltas: &mut HashMap<u32, Split>,
        notes: &mut Vec<String>,
    ) -> HashMap<u32, Vec<Heir>> {
        let alive = |pid: u32, start: u64| {
            current
                .get(&pid)
                .is_some_and(|now| now.start_ticks == start)
        };
        let lookup = |pid: u32| previous.get(&pid).map(|seen| (seen.start_ticks, seen.ppid));
        let mut due: HashMap<u32, IoCounters> = HashMap::new();
        let mut heirs: HashMap<u32, Vec<Heir>> = HashMap::new();
        for (&pid, gone) in previous {
            if alive(pid, gone.start_ticks) {
                continue;
            }
            let Some(to) = receiver(pid, lookup, alive, previous.len()) else {
                continue;
            };
            let lifetime = gone.counters.map_or_else(IoCounters::default, |counters| {
                let reaped = gone.reaped.unwrap_or_default();
                IoCounters {
                    read_bytes: counters.read_bytes.saturating_sub(reaped.read_bytes),
                    write_bytes: counters.write_bytes.saturating_sub(reaped.write_bytes),
                }
            });
            heirs.entry(to).or_default().push(Heir {
                pid,
                name: gone.name.clone(),
                renamed_from: gone.renamed_from.clone(),
                last: gone.last,
                lifetime,
            });
            if let Some(counters) = gone.counters {
                add(due.entry(to).or_default(), counters);
            }
        }
        for list in heirs.values_mut() {
            list.sort_by_key(|heir| heir.pid);
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
            let left = take_back_split(delta, counters);
            if !is_zero(left) {
                let start = current.get(&to).map_or(0, |seen| seen.start_ticks);
                self.carried.insert(to, (start, left));
            }
        }
        // Last snapshot's remainders: once more, then dropped.
        for (to, (start, counters)) in carried {
            let left = match deltas.get_mut(&to) {
                Some(delta) if alive(to, start) => take_back_split(delta, counters),
                _ => counters,
            };
            if !is_zero(left) {
                notes.push(format!(
                    "{left:?} of a reaped child's counters could not be taken back from pid {to} \
                     (was it the reaper?); dropped"
                ));
            }
        }
        heirs
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

    /// A process as a version-1 helper wrote it (no reaped part): `(pid, name, ppid, start, read,
    /// write)`.
    pub fn entry(pid: u32, name: &str, ppid: u32, start: u64, read: u64, write: u64) -> Entry {
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
            reaped: None,
            cminflt: 0,
            name: name.into(),
        }
    }

    /// A process as a version-2 helper writes it, writing only: `write` bytes in all, of which
    /// `reaped` came from exited threads and reaped children; `cminflt` its reaped children's
    /// faults.
    pub fn writer(
        pid: u32,
        name: &str,
        ppid: u32,
        start: u64,
        write: u64,
        reaped: u64,
        cminflt: u64,
    ) -> Entry {
        Entry {
            reaped: Some(IoCounters {
                read_bytes: 0,
                write_bytes: reaped,
            }),
            cminflt,
            ..entry(pid, name, ppid, start, 0, write)
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
    /// snapshot already credited to the child is taken back, so it is counted once. A version-1
    /// snapshot has no reaped part, so tar's last 20 MB stay with bash (the rule before #1572).
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

    /// The same reap with a version-2 snapshot (#1572): bash's reaped part grew by tar's final
    /// 120 MB and its `cminflt` grew, so after taking tar's 100 MB back, tar's last 20 MB are
    /// tar's — bash keeps only its own 2 MB. The scan scenarios in `hsm_linux_probe_io::scan`
    /// (`bash_and_tar`) pin that the helper writes exactly this whatever the read order.
    #[test]
    fn a_reaped_childs_last_interval_is_credited_to_the_child() {
        let mut feed = Feed::new(vec![
            writer(10, "bash", 1, 5, MB, 0, 0),
            writer(11, "tar", 10, 6, 100 * MB, 0, 0),
        ]);
        let read = feed.next(vec![writer(10, "bash", 1, 5, 123 * MB, 120 * MB, 1)]);
        assert_eq!(read.deltas["bash"].written, 2 * MB, "{:?}", read.deltas);
        assert_eq!(read.deltas["tar"].written, 20 * MB);
        assert_eq!(read.deltas["tar"].top_writer, (NO_PROCESS, 20 * MB));
        assert!(!read.deltas.contains_key("children of bash"));
        let next = feed.next(vec![writer(10, "bash", 1, 5, 123 * MB, 120 * MB, 1)]);
        assert!(next.deltas.is_empty(), "no late jump");
    }

    /// The night backup of #1572: a job systemd starts and reaps within one interval is never
    /// seen, so its bytes cannot carry its name — they are `children of systemd`, not systemd's.
    /// systemd's own bytes stay its own.
    #[test]
    fn bytes_of_children_no_snapshot_saw_go_to_children_of_the_reaper() {
        let mut feed = Feed::new(vec![writer(1, "systemd", 0, 1, 50 * MB, 40 * MB, 1000)]);
        let read = feed.next(vec![writer(1, "systemd", 0, 1, 351 * MB, 340 * MB, 1012)]);
        assert_eq!(read.deltas["children of systemd"].written, 300 * MB);
        assert_eq!(
            read.deltas["children of systemd"].top_writer.0, NO_PROCESS,
            "no path line"
        );
        assert_eq!(read.deltas["systemd"].written, MB, "its own");
        assert_eq!(read.deltas.len(), 2);
    }

    /// The reaped part also grows when a thread exits (its counters move there); without a reap
    /// (`cminflt` unchanged) that is the process's own I/O, never `children of …`.
    #[test]
    fn exited_threads_bytes_are_the_process_own() {
        let mut feed = Feed::new(vec![writer(30, "java", 1, 5, 10 * MB, 0, 3)]);
        let read = feed.next(vec![writer(30, "java", 1, 5, 60 * MB, 50 * MB, 3)]);
        assert_eq!(read.deltas["java"].written, 50 * MB);
        assert_eq!(read.deltas.len(), 1, "{:?}", read.deltas);
    }

    /// `cron → sh → tar` exit within one interval: cron's reaped part holds both final totals.
    /// After taking back what they were credited, what is left is theirs, shared by what each
    /// moved in its last interval (sh 1 MB, tar 50 MB) — none of it is cron's.
    #[test]
    fn a_job_tree_exiting_at_once_is_credited_to_its_seen_members() {
        let mut feed = Feed::new(vec![
            writer(20, "cron", 1, 5, 0, 0, 0),
            writer(21, "sh", 20, 6, MB, 0, 0),
            writer(22, "tar", 21, 7, 400 * MB, 0, 0),
        ]);
        let read = feed.next(vec![
            writer(20, "cron", 1, 5, 0, 0, 0),
            writer(21, "sh", 20, 6, 2 * MB, 0, 0),
            writer(22, "tar", 21, 7, 450 * MB, 0, 0),
        ]);
        assert_eq!(read.deltas["tar"].written, 50 * MB);
        // sh: 3 MB of its own + tar's final 500 MB; cron: 2 MB of its own + sh's final 503 MB.
        let read = feed.next(vec![writer(20, "cron", 1, 5, 505 * MB, 503 * MB, 2)]);
        assert_eq!(read.deltas["cron"].written, 2 * MB, "{:?}", read.deltas);
        assert_eq!(read.deltas["sh"].written, MB);
        assert_eq!(read.deltas["tar"].written, 50 * MB);
        assert!(!read.deltas.contains_key("children of cron"));
        let total: u64 = read.deltas.values().map(|io| io.written).sum();
        assert_eq!(total, 53 * MB, "every byte once");
    }

    /// A version-1 snapshot followed by a version-2 one (the helper restarted on an upgrade): no
    /// split until both ends of an interval know the reaped part.
    #[test]
    fn the_split_needs_a_reaped_part_at_both_ends() {
        let mut feed = Feed::new(vec![entry(1, "systemd", 0, 1, 0, 50 * MB)]);
        let read = feed.next(vec![writer(1, "systemd", 0, 1, 80 * MB, 70 * MB, 9)]);
        assert_eq!(read.deltas["systemd"].written, 30 * MB);
        let read = feed.next(vec![writer(1, "systemd", 0, 1, 90 * MB, 80 * MB, 10)]);
        assert_eq!(read.deltas["children of systemd"].written, 10 * MB);
        assert!(!read.deltas.contains_key("systemd"));
    }

    /// A name the helper extended from a 15-byte `comm` (#1573) is reported with the name it
    /// replaces, for the one-time log line; a short name, a kernel thread and a name that was
    /// never cut are not.
    #[test]
    fn an_extended_name_is_reported_with_the_name_it_replaces() {
        let mut kworker = entry(3, "kworker/u8:2-flush-8:0", 2, 1, 0, 0);
        kworker.kernel_thread = true;
        let mut feed = Feed::new(vec![
            writer(40, "valheim_server.x86_64", 1, 5, 0, 0, 0),
            writer(41, "nginx", 1, 6, 0, 0, 0),
            kworker.clone(),
        ]);
        kworker.counters = Some(IoCounters {
            read_bytes: 0,
            write_bytes: MB,
        });
        let read = feed.next(vec![
            writer(40, "valheim_server.x86_64", 1, 5, 9 * MB, 0, 0),
            writer(41, "nginx", 1, 6, MB, 0, 0),
            kworker,
        ]);
        assert_eq!(read.deltas["valheim_server.x86_64"].written, 9 * MB);
        assert_eq!(
            read.renamed,
            [(
                "valheim_server.x86_64".to_string(),
                "valheim_server.".to_string()
            )]
            .into()
        );
    }

    /// A live process stays a name's representative over a larger share of a vanished one (or
    /// `children of …`), in either merge order, so the sensor gets a path line (review of #1572).
    #[test]
    fn a_live_process_beats_no_process_as_the_representative() {
        let live = |bytes| NameIo {
            written: bytes,
            top_writer: (12, bytes),
            ..NameIo::default()
        };
        let share = |bytes| NameIo {
            written: bytes,
            top_writer: (NO_PROCESS, bytes),
            ..NameIo::default()
        };
        let mut io = live(MB);
        io.merge(&share(20 * MB));
        assert_eq!(io.top_writer, (12, MB));
        let mut io = share(20 * MB);
        io.merge(&live(MB));
        assert_eq!((io.top_writer, io.written), ((12, MB), 21 * MB));
        let mut io = share(MB);
        io.merge(&share(20 * MB));
        assert_eq!(io.top_writer, (NO_PROCESS, 20 * MB));

        // In a snapshot: a dead tar's last 20 MB and a live tar's 1 MB under one name.
        let mut feed = Feed::new(vec![
            writer(10, "bash", 1, 5, 0, 0, 0),
            writer(11, "tar", 10, 6, 100 * MB, 0, 0),
            writer(12, "tar", 10, 7, 0, 0, 0),
        ]);
        let read = feed.next(vec![
            writer(10, "bash", 1, 5, 120 * MB, 120 * MB, 1),
            writer(12, "tar", 10, 7, MB, 0, 0),
        ]);
        assert_eq!(read.deltas["tar"].written, 21 * MB);
        assert_eq!(read.deltas["tar"].top_writer.0, 12);
    }

    /// A `comm` whose 15-byte cut split a multibyte character arrives lossily decoded, longer than
    /// 15 bytes; it was never extended, so it is not reported as renamed (review of #1573).
    #[test]
    fn a_comm_cut_inside_a_multibyte_character_is_not_a_rename() {
        let cut = "sauvegarde_bdd\u{fffd}";
        let middle = String::from_utf8_lossy(b"a\xe9bcdefghijklmn").into_owned();
        let mut feed = Feed::new(vec![
            writer(50, cut, 1, 5, 0, 0, 0),
            writer(51, &middle, 1, 6, 0, 0, 0),
            writer(52, "sauvegarde_bdd\u{e9}-nuit", 1, 7, 0, 0, 0),
        ]);
        let read = feed.next(vec![
            writer(50, cut, 1, 5, MB, 0, 0),
            writer(51, &middle, 1, 6, MB, 0, 0),
            writer(52, "sauvegarde_bdd\u{e9}-nuit", 1, 7, MB, 0, 0),
        ]);
        assert_eq!(read.deltas.len(), 3, "{:?}", read.deltas);
        assert_eq!(
            read.renamed,
            [(
                "sauvegarde_bdd_-nuit".to_string(),
                "sauvegarde_bdd_".to_string()
            )]
            .into(),
            "only the extended name"
        );
    }

    #[test]
    fn shares_add_up_to_the_total() {
        assert_eq!(shares(10, &[1, 3]), [2, 8]);
        assert_eq!(shares(10, &[0, 0, 0]), [4, 3, 3]);
        assert_eq!(shares(0, &[5]), [0]);
        assert_eq!(shares(7, &[2, 2]), [4, 3], "the first on a tie");
        assert_eq!(
            shares(u64::MAX, &[u64::MAX, 1]).iter().sum::<u64>(),
            u64::MAX
        );
    }

    /// End to end through the helper's real scan of a fake `/proc` (#1572): a backup started by
    /// systemd, seen once, exits with its `tar` within one interval — their last bytes are
    /// theirs; then a job too short to be seen is `children of systemd`. systemd is never named.
    #[test]
    fn the_helpers_scan_feeds_the_attribution_end_to_end() {
        use crate::probe_only::host::tests::FakeTree;
        use hsm_linux_probe_io::scan::Scanner;

        let tree = FakeTree::new("io-e2e");
        let process = |pid: u32, name: &str, ppid: u32, start: u64, cminflt: u64| {
            tree.file(
                &format!("{pid}/stat"),
                &format!(
                    "{pid} ({name}) S {ppid} {pid} {pid} 0 -1 4194560 0 {cminflt} 0 0 1 1 0 0 20 \
                     0 1 0 {start} 1 1 1 0 0 0 0 0 0 0 0 0 0 0\n"
                ),
            );
        };
        let io = |relative: &str, write: u64| {
            tree.file(
                relative,
                &format!("rchar: 0\nwchar: 0\nread_bytes: 0\nwrite_bytes: {write}\n"),
            );
        };
        let mut scanner = Scanner::new(&tree.0);
        let mut sampler = IoSampler::new();
        let mut seq = 0;
        let mut next = |scanner: &mut Scanner| {
            seq += 1;
            sampler.consume(&snapshot(seq, scanner.scan().expect("scan").entries))
        };

        process(1, "systemd", 0, 1, 100);
        io("1/io", 5 * MB);
        io("1/task/1/io", 5 * MB);
        process(100, "backup.sh", 1, 50, 0);
        io("100/io", 0);
        io("100/task/100/io", 0);
        process(101, "tar", 100, 51, 0);
        io("101/io", 100 * MB);
        io("101/task/101/io", 100 * MB);
        assert!(next(&mut scanner).baseline);

        // tar wrote 50 MB more and was reaped by backup.sh, which was reaped by systemd.
        for gone in ["100", "101"] {
            std::fs::remove_dir_all(tree.0.join(gone)).expect("reaped");
        }
        process(1, "systemd", 0, 1, 102);
        io("1/io", 155 * MB);
        let read = next(&mut scanner);
        assert_eq!(read.deltas["tar"].written, 50 * MB, "{:?}", read.deltas);
        assert!(!read.deltas.contains_key("systemd"), "{:?}", read.deltas);
        assert!(!read.deltas.contains_key("children of systemd"));

        // A job that lived less than one interval: 30 MB nobody saw.
        process(1, "systemd", 0, 1, 103);
        io("1/io", 185 * MB);
        let read = next(&mut scanner);
        assert_eq!(read.deltas["children of systemd"].written, 30 * MB);
        assert_eq!(read.deltas.len(), 1, "{:?}", read.deltas);
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
