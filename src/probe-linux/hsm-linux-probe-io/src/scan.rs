//! One scan of every process's `stat` and `io`, made race-free against reaps in the middle of it.
//!
//! The kernel adds a reaped child's whole I/O counters to its reaper (`wait_task_zombie`) and only
//! then removes the child's pid from `/proc` (`release_task`). The probe takes a vanished
//! process's last-read counters back from its reaper (they would count twice), which is right
//! only if the reaper's value already holds the jump. A scan is not atomic, so after it every
//! receiver ([`receiver`]) of a process that vanished since the previous scan has its `io` read
//! **again**: that value holds every such jump, whatever order the scan read the two in. A
//! process whose `stat` was read but whose `io` is gone by the time it is read (reaped in between)
//! counts as vanished in this scan; one whose `io` read fails while it is still there is written
//! with unknown counters (the probe keeps its previous ones). A child read **before** its parent (a
//! parent with a higher pid: after a pid wrap, or a late subreaper) may be reaped before the
//! parent is read, so it would be listed and in the parent's value at once: after the scan every
//! such child is looked up again, and one that is gone is dropped and its receiver read again.
//!
//! # The reaped part (#1572)
//!
//! `/proc/<pid>/io` is the live threads' counters plus `signal->ioac`, where the kernel keeps what
//! exited threads and reaped children did; `/proc/<pid>/task/<tid>/io` is one live thread's own.
//! So `io` minus the sum of the live threads' is the **reaped part** — which lets the probe credit
//! a reaped child's bytes to the child's name instead of the reaper's. Each process's `cminflt`
//! (stat field 11) is written with it: it grows with every reap and not when a thread exits. The
//! kernel adds a child's faults and counters in one step, and the stat was read before the `io`,
//! so a process with a reaped part has its stat read **again** after its `io` (a `cminflt` at
//! least as new as the counters); gone by then, it counts as vanished like one whose `io` is gone.
//! A receiver read again after the scan has all three read again, in that order.
//!
//! # The name (#1573)
//!
//! A 15-byte `comm` may be cut; the `exe` link (readable with `CAP_SYS_PTRACE`) gives the
//! executable's basename, which replaces it when it extends it ([`extended_name`]).

use std::collections::{HashMap, HashSet};
use std::io::ErrorKind;
use std::path::{Path, PathBuf};

use crate::procstat::{extended_name, read_pid_stat, read_processes, PidStat, COMM_LEN};
use crate::snapshot::{parse_io, Entry, IoCounters};

/// The process a vanished `pid` was reaped by, as far as two reads can tell: its parent at the
/// previous read when that parent is still alive (same start time) — a shell, `cron`, `systemd`
/// or a container shim reaping its child, the normal case. When the parent vanished too, the
/// child's counters went into the parent's before the parent was reaped in turn (a job tree
/// exiting at once: `cron → sh → tar`), so the walk continues up the previous read's parents to
/// the first one still alive. `None` when the chain leaves the processes the previous read saw.
///
/// `previous(pid)` gives `(start_ticks, ppid)` at the previous read; `alive(pid, start_ticks)`
/// says whether that process is in the current one. An orphan is reaped by its subreaper or by
/// `init`, not by its old grandparent; when its parent died first **and** both vanished within one
/// interval, the walk names the grandparent instead — a rare case, documented in the README.
pub fn receiver(
    pid: u32,
    previous: impl Fn(u32) -> Option<(u64, u32)>,
    alive: impl Fn(u32, u64) -> bool,
    max_steps: usize,
) -> Option<u32> {
    let mut ppid = previous(pid)?.1;
    // Every step moves to a process of the previous read, so its size bounds the walk (a pid
    // table cannot hold a cycle, but a torn read could).
    for _ in 0..max_steps {
        let (start, grandparent) = previous(ppid)?;
        if alive(ppid, start) {
            return Some(ppid);
        }
        ppid = grandparent;
    }
    None
}

fn is_zero(counters: IoCounters) -> bool {
    counters.read_bytes == 0 && counters.write_bytes == 0
}

/// What one scan found.
#[derive(Debug, Default)]
pub struct Scan {
    pub entries: Vec<Entry>,
    /// `io` reads refused (`EACCES`): the helper lacks its capabilities.
    pub denied: usize,
    /// Receivers whose `io` was read again after the scan.
    pub reread: usize,
}

/// Scans `<proc>` and remembers each scan's `(pid → start_ticks, ppid)` for the next one.
pub struct Scanner {
    proc_root: PathBuf,
    previous: Option<HashMap<u32, (u64, u32)>>,
    /// Reads one `<pid>/io`; injectable so a reap in the middle of a scan can be tested.
    read_io: fn(&Path) -> std::io::Result<String>,
}

fn read_io_file(path: &Path) -> std::io::Result<String> {
    std::fs::read_to_string(path)
}

impl Scanner {
    pub fn new(proc_root: impl Into<PathBuf>) -> Self {
        Self::with_reader(proc_root, read_io_file)
    }

    pub fn with_reader(
        proc_root: impl Into<PathBuf>,
        read_io: fn(&Path) -> std::io::Result<String>,
    ) -> Self {
        Self {
            proc_root: proc_root.into(),
            previous: None,
            read_io,
        }
    }

    fn read_counters(&self, pid: u32) -> std::io::Result<Option<IoCounters>> {
        (self.read_io)(&self.proc_root.join(pid.to_string()).join("io")).map(|text| parse_io(&text))
    }

    /// The reaped part of `whole` (module documentation): `whole` minus every live thread's own
    /// counters, each counter never below 0. A thread that exits between the listing and its read
    /// is skipped (its counters moved into the reaped part). `None` when the threads cannot be
    /// listed or one cannot be read.
    fn read_reaped(&self, pid: u32, whole: IoCounters) -> Option<IoCounters> {
        let task = self.proc_root.join(pid.to_string()).join("task");
        let mut live = IoCounters::default();
        let mut threads = 0usize;
        for entry in std::fs::read_dir(&task).ok()?.flatten() {
            if entry.file_name().to_str()?.parse::<u32>().is_err() {
                continue;
            }
            match (self.read_io)(&entry.path().join("io")) {
                Ok(text) => {
                    let own = parse_io(&text)?;
                    live.read_bytes = live.read_bytes.saturating_add(own.read_bytes);
                    live.write_bytes = live.write_bytes.saturating_add(own.write_bytes);
                    threads += 1;
                }
                Err(error) if error.kind() == ErrorKind::NotFound => {}
                Err(_) => return None,
            }
        }
        (threads > 0).then(|| IoCounters {
            read_bytes: whole.read_bytes.saturating_sub(live.read_bytes),
            write_bytes: whole.write_bytes.saturating_sub(live.write_bytes),
        })
    }

    /// `(reaped part, cminflt)` of a process whose `io` read gave `whole`; `None` when it is gone
    /// (or its pid reused) by the time its stat is read again.
    fn read_reaped_and_faults(
        &self,
        stat: &PidStat,
        whole: Option<IoCounters>,
    ) -> Option<(Option<IoCounters>, u64)> {
        let reaped = match whole {
            Some(whole) if !stat.is_kernel_thread() => self.read_reaped(stat.pid, whole),
            _ => None,
        };
        if reaped.is_none_or(is_zero) {
            return Some((reaped, stat.cminflt));
        }
        let now = read_pid_stat(&self.proc_root, stat.pid)
            .filter(|now| now.start_ticks == stat.start_ticks)?;
        Some((reaped, now.cminflt))
    }

    /// `comm`, or the `exe` link's basename when it extends a 15-byte one.
    fn name(&self, stat: &PidStat) -> String {
        if stat.is_kernel_thread() || stat.comm.len() != COMM_LEN {
            return stat.comm.clone();
        }
        std::fs::read_link(self.proc_root.join(stat.pid.to_string()).join("exe"))
            .ok()
            .and_then(|exe| extended_name(&stat.comm, &exe.to_string_lossy()))
            .unwrap_or_else(|| stat.comm.clone())
    }

    /// One scan; an error only when `<proc>` cannot be listed.
    pub fn scan(&mut self) -> Result<Scan, String> {
        let processes = read_processes(&self.proc_root)?;
        let mut scan = Scan::default();
        for stat in processes {
            let counters = match self.read_counters(stat.pid) {
                Ok(counters) => counters,
                Err(error) if error.kind() == ErrorKind::PermissionDenied => {
                    scan.denied += 1;
                    None
                }
                // Reaped since its stat was read: already in its reaper's counters — vanished.
                Err(_)
                    if read_pid_stat(&self.proc_root, stat.pid)
                        .is_none_or(|now| now.start_ticks != stat.start_ticks) =>
                {
                    continue
                }
                Err(_) => None,
            };
            // Gone when its stat is read again: reaped after its `io` read — vanished too.
            let Some((reaped, cminflt)) = self.read_reaped_and_faults(&stat, counters) else {
                continue;
            };
            scan.entries.push(Entry {
                pid: stat.pid,
                start_ticks: stat.start_ticks,
                ppid: stat.ppid,
                kernel_thread: stat.is_kernel_thread(),
                sigchld_ignored: stat.ignores_sigchld(),
                counters,
                reaped,
                cminflt,
                name: self.name(&stat),
            });
        }

        // A child read BEFORE its parent (a higher parent pid: after a pid wrap, or a subreaper
        // started late) and reaped before the parent was read is listed AND already in the
        // parent's value. So every such child is looked at again now: gone, it is dropped and its
        // receiver read again below, like a child that vanished before the scan.
        let in_scan: HashMap<u32, (u64, u32)> = scan
            .entries
            .iter()
            .map(|entry| (entry.pid, (entry.start_ticks, entry.ppid)))
            .collect();
        let reaped_in_scan: Vec<u32> = scan
            .entries
            .iter()
            .filter(|entry| entry.ppid > entry.pid)
            .filter(|entry| {
                read_pid_stat(&self.proc_root, entry.pid)
                    .is_none_or(|now| now.start_ticks != entry.start_ticks)
            })
            .map(|entry| entry.pid)
            .collect();
        scan.entries
            .retain(|entry| !reaped_in_scan.contains(&entry.pid));

        let index: HashMap<u32, usize> = scan
            .entries
            .iter()
            .enumerate()
            .map(|(i, entry)| (entry.pid, i))
            .collect();
        let alive = |pid: u32, start: u64| {
            index
                .get(&pid)
                .is_some_and(|&i| scan.entries[i].start_ticks == start)
        };
        let mut receivers = HashSet::new();
        for &pid in &reaped_in_scan {
            let lookup = |p: u32| in_scan.get(&p).copied();
            if let Some(to) = receiver(pid, lookup, alive, in_scan.len()) {
                receivers.insert(to);
            }
        }
        if let Some(previous) = &self.previous {
            for (&pid, &(start, _)) in previous {
                if alive(pid, start) {
                    continue;
                }
                let lookup = |p: u32| previous.get(&p).copied();
                if let Some(to) = receiver(pid, lookup, alive, previous.len()) {
                    receivers.insert(to);
                }
            }
        }
        for to in receivers {
            let (Some(&i), Ok(Some(counters))) = (index.get(&to), self.read_counters(to)) else {
                continue;
            };
            let entry = &mut scan.entries[i];
            entry.counters = Some(counters);
            entry.reaped = if entry.kernel_thread {
                None
            } else {
                self.read_reaped(to, counters)
            };
            // After the counters, so it is at least as new as they are (module documentation).
            if let Some(now) = read_pid_stat(&self.proc_root, to)
                .filter(|now| now.start_ticks == entry.start_ticks)
            {
                entry.cminflt = now.cminflt;
            }
            scan.reread += 1;
        }
        self.previous = Some(
            scan.entries
                .iter()
                .map(|entry| (entry.pid, (entry.start_ticks, entry.ppid)))
                .collect(),
        );
        Ok(scan)
    }
}

#[cfg(test)]
pub mod tests {
    use super::*;

    const MB: u64 = 1_048_576;

    /// A throwaway `/proc`.
    pub struct Tree(pub PathBuf);

    impl Tree {
        pub fn new(tag: &str) -> Self {
            use std::sync::atomic::{AtomicUsize, Ordering};
            static NEXT: AtomicUsize = AtomicUsize::new(0);
            let path = std::env::temp_dir().join(format!(
                "hsm-io-{tag}-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::SeqCst)
            ));
            let _ = std::fs::remove_dir_all(&path);
            std::fs::create_dir_all(&path).expect("temp dir");
            Self(path)
        }

        pub fn file(&self, relative: &str, text: &str) {
            let path = self.0.join(relative);
            std::fs::create_dir_all(path.parent().expect("parent")).expect("mkdir");
            std::fs::write(path, text).expect("write");
        }

        /// A single-threaded process that reaped nothing yet: its `stat` (parent, start time,
        /// `cminflt` 0), its `io` and its one thread's `io` (the same: all of it is its own).
        pub fn process(&self, pid: u32, comm: &str, ppid: u32, start: u64, write: u64) {
            self.stat(pid, comm, ppid, start, 0);
            self.file(&format!("{pid}/io"), &io_text(write));
            self.own(pid, write);
        }

        /// The `stat` alone, with the reaped children's minor faults (field 11).
        pub fn stat(&self, pid: u32, comm: &str, ppid: u32, start: u64, cminflt: u64) {
            self.file(
                &format!("{pid}/stat"),
                &format!(
                    "{pid} ({comm}) S {ppid} {pid} {pid} 0 -1 4194560 0 {cminflt} 0 0 1 1 0 0 20 \
                     0 1 0 {start} 1 1 1 0 0 0 0 0 0 0 0 0 0 0\n"
                ),
            );
        }

        /// The main thread's own counters (`<pid>/task/<pid>/io`).
        pub fn own(&self, pid: u32, write: u64) {
            self.file(&format!("{pid}/task/{pid}/io"), &io_text(write));
        }
    }

    impl Drop for Tree {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    fn io_text(write: u64) -> String {
        format!("rchar: 9\nwchar: 9\nread_bytes: 0\nwrite_bytes: {write}\n")
    }

    /// What the kernel does to the reaper when it reaps: the reaper's `io` becomes `write` (its
    /// threads' own counters stay) and its `cminflt` (stat field 11) grows, in one step.
    fn reap_into(root: &Path, reaper: u64, write: u64) -> std::io::Result<()> {
        let dir = root.join(reaper.to_string());
        std::fs::write(dir.join("io"), io_text(write))?;
        let stat = std::fs::read_to_string(dir.join("stat"))?;
        let close = stat.rfind(')').expect("stat");
        let mut rest: Vec<String> = stat[close + 1..]
            .split_whitespace()
            .map(str::to_string)
            .collect();
        // rest[0] is field 3, so field 11 is rest[8].
        let faults: u64 = rest[8].parse().expect("cminflt");
        rest[8] = (faults + 1).to_string();
        std::fs::write(
            dir.join("stat"),
            format!("{} {}\n", &stat[..=close], rest.join(" ")),
        )
    }

    /// The kernel, scripted by what an `io` file holds:
    ///
    /// * `REAP <reaper> <write>` — reaped **between its `stat` and `io` reads**: the reaper's
    ///   counter becomes `<write>` ([`reap_into`]), the pid disappears, `ENOENT`;
    /// * `THEN-REAP <child> <write>` on the first line — this (the reaper's) `io` is read first,
    ///   then `<child>` is reaped: the rest is returned, the reaper's counter becomes `<write>`;
    /// * `READ-THEN-REAPED <reaper> <write>` on the first line — this `io` is read, then the
    ///   process is reaped into `<reaper>`: the rest is returned, the pid disappears;
    /// * `ENOENT` — a read that fails while the process is still there.
    pub fn scripted(path: &Path) -> std::io::Result<String> {
        let text = std::fs::read_to_string(path)?;
        let dir = path.parent().expect("pid dir");
        let root = dir.parent().expect("proc root");
        let numbers = |rest: &str| -> Vec<u64> {
            rest.split_whitespace()
                .map(|n| n.parse().expect("number"))
                .collect()
        };
        if text == "ENOENT" {
            return Err(std::io::Error::from(ErrorKind::NotFound));
        }
        if let Some(rest) = text.strip_prefix("REAP ") {
            let n = numbers(rest);
            reap_into(root, n[0], n[1])?;
            std::fs::remove_dir_all(dir)?;
            return Err(std::io::Error::from(ErrorKind::NotFound));
        }
        if let Some(rest) = text.strip_prefix("THEN-REAP ") {
            let (first, io) = rest.split_once('\n').expect("two parts");
            let n = numbers(first);
            std::fs::remove_dir_all(root.join(n[0].to_string()))?;
            let this: u64 = dir
                .file_name()
                .and_then(|n| n.to_str()?.parse().ok())
                .expect("pid");
            reap_into(root, this, n[1])?;
            return Ok(io.to_string());
        }
        if let Some(rest) = text.strip_prefix("READ-THEN-REAPED ") {
            let (first, io) = rest.split_once('\n').expect("two parts");
            let n = numbers(first);
            reap_into(root, n[0], n[1])?;
            std::fs::remove_dir_all(dir)?;
            return Ok(io.to_string());
        }
        Ok(text)
    }

    fn listed(scan: &Scan, pid: u32) -> &Entry {
        scan.entries
            .iter()
            .find(|entry| entry.pid == pid)
            .unwrap_or_else(|| panic!("pid {pid} not listed: {:?}", scan.entries))
    }

    fn written(scan: &Scan, pid: u32) -> Option<u64> {
        scan.entries
            .iter()
            .find(|entry| entry.pid == pid)
            .and_then(|entry| entry.counters)
            .map(|counters| counters.write_bytes)
    }

    /// The written bytes of the reaped part and `cminflt`: what the probe tells a reaped child's
    /// bytes from the reaper's own by (#1572).
    fn reaped(scan: &Scan, pid: u32) -> (Option<u64>, u64) {
        let entry = listed(scan, pid);
        (entry.reaped.map(|r| r.write_bytes), entry.cminflt)
    }

    /// bash (`reaper`) and its child tar (11) at the first scan; tar then wrote 20 MB more and was
    /// reaped: bash's counter = its own 3 MB + tar's final 120 MB.
    fn bash_and_tar(tag: &str, reaper: u32) -> (Tree, Scanner) {
        let tree = Tree::new(tag);
        tree.process(reaper, "bash", 1, 5, MB);
        tree.process(11, "tar", reaper, 6, 100 * MB);
        let mut scanner = Scanner::with_reader(&tree.0, scripted);
        let first = scanner.scan().expect("first scan");
        assert_eq!(reaped(&first, reaper), (Some(0), 0), "nothing reaped yet");
        (tree, scanner)
    }

    const AFTER_REAP: u64 = 123 * MB;
    /// tar's final counters, all in bash's reaped part after the reap (bash's own 3 MB are not).
    const TAR_FINAL: u64 = 120 * MB;

    /// The reaper's `io` is read, then the child is reaped before its own `io` is read: the
    /// snapshot still carries the reaper's post-reap value (read again after the scan) — its
    /// reaped part and `cminflt` included, so tar's bytes can be told from bash's own.
    #[test]
    fn a_child_reaped_right_after_its_parents_read_leaves_the_reread_value() {
        let (tree, mut scanner) = bash_and_tar("race-a", 10);
        tree.own(10, 3 * MB);
        tree.file(
            "10/io",
            &format!("THEN-REAP 11 {AFTER_REAP}\n{}", io_text(3 * MB)),
        );
        let scan = scanner.scan().expect("scan");
        assert_eq!(written(&scan, 10), Some(AFTER_REAP));
        assert_eq!(reaped(&scan, 10), (Some(TAR_FINAL), 1));
        assert!(scan.entries.iter().all(|entry| entry.pid != 11));
        assert_eq!(scan.reread, 1);
    }

    /// The child's `stat` is read, then it is reaped before its `io` is read: it counts as
    /// vanished in this scan, and its reaper is read again.
    #[test]
    fn a_child_reaped_between_its_stat_and_io_reads_is_vanished() {
        let (tree, mut scanner) = bash_and_tar("race-b", 10);
        tree.process(10, "bash", 1, 5, 3 * MB);
        tree.file("11/io", &format!("REAP 10 {AFTER_REAP}"));
        let scan = scanner.scan().expect("scan");
        assert_eq!(written(&scan, 10), Some(AFTER_REAP));
        assert_eq!(reaped(&scan, 10), (Some(TAR_FINAL), 1));
        assert!(scan.entries.iter().all(|entry| entry.pid != 11));
    }

    /// After a pid wrap the parent has the higher pid, so the scan reads the child first. The
    /// parent's stat was read before the reap: it is read again after its `io`, so its `cminflt`
    /// is as new as its counters.
    #[test]
    fn a_parent_with_a_higher_pid_than_its_child_is_read_after_the_reap() {
        let (tree, mut scanner) = bash_and_tar("race-c", 900);
        tree.process(900, "bash", 1, 5, 3 * MB);
        tree.file("11/io", &format!("REAP 900 {AFTER_REAP}"));
        let scan = scanner.scan().expect("scan");
        assert_eq!(written(&scan, 900), Some(AFTER_REAP));
        assert_eq!(reaped(&scan, 900), (Some(TAR_FINAL), 1));
        assert_eq!(scan.entries.len(), 1);
    }

    /// The inverse race: the child (lower pid) is read in full, THEN reaped, then its parent (higher
    /// pid) is read with the jump in it. Listing both would count the child twice; the child is
    /// looked at again after the scan and dropped, and its parent read again.
    #[test]
    fn a_child_read_then_reaped_before_its_higher_pid_parent_is_dropped() {
        let (tree, mut scanner) = bash_and_tar("race-inverse", 900);
        tree.process(900, "bash", 1, 5, 3 * MB);
        tree.file(
            "11/io",
            &format!("READ-THEN-REAPED 900 {AFTER_REAP}\n{}", io_text(TAR_FINAL)),
        );
        let scan = scanner.scan().expect("scan");
        assert!(
            scan.entries.iter().all(|entry| entry.pid != 11),
            "tar dropped"
        );
        assert_eq!(written(&scan, 900), Some(AFTER_REAP));
        assert_eq!(reaped(&scan, 900), (Some(TAR_FINAL), 1));
        assert_eq!(scan.reread, 1);
    }

    /// A job tree `cron(20) → sh(21) → tar(22)` exits within one interval: sh reaped tar (500 MB)
    /// before the scan, and is itself reaped into cron right after cron's `io` is read. cron is
    /// read again; everything both did is in its reaped part, none of it in its own counters.
    #[test]
    fn a_job_tree_exiting_at_once_lands_in_the_survivors_reaped_part() {
        let tree = Tree::new("tree");
        tree.process(20, "cron", 1, 5, 0);
        tree.process(21, "sh", 20, 6, MB);
        tree.process(22, "tar", 21, 7, 400 * MB);
        let mut scanner = Scanner::with_reader(&tree.0, scripted);
        scanner.scan().expect("first scan");

        // tar wrote 100 MB more and was reaped by sh; sh wrote 10 MB more (11 MB of its own).
        std::fs::remove_dir_all(tree.0.join("22")).expect("tar reaped");
        tree.stat(21, "sh", 20, 6, 1);
        tree.file("21/io", &io_text(11 * MB + 500 * MB));
        tree.own(21, 11 * MB);
        // cron wrote 2 MB of its own, then reaps sh right after its `io` is read.
        tree.own(20, 2 * MB);
        tree.file(
            "20/io",
            &format!("THEN-REAP 21 {}\n{}", 513 * MB, io_text(2 * MB)),
        );
        let scan = scanner.scan().expect("scan");
        assert_eq!(scan.entries.len(), 1, "{:?}", scan.entries);
        assert_eq!(written(&scan, 20), Some(513 * MB));
        assert_eq!(
            reaped(&scan, 20),
            (Some(511 * MB), 1),
            "sh's 11 MB and tar's 500 MB, not cron's own 2 MB"
        );
        assert_eq!(scan.reread, 1, "cron, once, for both");
    }

    /// The reaped part is the whole minus every live thread's own counters; a thread that exited
    /// between the listing and its read is skipped; a kernel thread has none; a process whose
    /// threads cannot be listed has an unknown one. A process with a reaped part has its `cminflt`
    /// from a stat read after its `io`.
    #[test]
    fn the_reaped_part_is_the_whole_minus_the_live_threads() {
        let tree = Tree::new("threads");
        tree.stat(40, "postgres", 1, 5, 7);
        tree.file("40/io", &io_text(15 * MB));
        tree.own(40, 2 * MB);
        tree.file("40/task/41/io", &io_text(3 * MB));
        tree.file("40/task/42/io", "ENOENT");
        // No task directory at all: unknown.
        tree.stat(50, "odd", 1, 6, 0);
        tree.file("50/io", &io_text(MB));
        tree.process(60, "kworker/0:1", 2, 1, 0);
        tree.file(
            "60/stat",
            "60 (kworker/0:1) I 2 0 0 0 -1 69238880 0 0 0 0 0 0 0 0 20 0 1 0 1 0 0 1 0 0 0 0 0 \
             0 0 2147483647 0 0 0\n",
        );
        let scan = Scanner::with_reader(&tree.0, scripted)
            .scan()
            .expect("scan");
        assert_eq!(reaped(&scan, 40), (Some(10 * MB), 7));
        assert_eq!(reaped(&scan, 50), (None, 0));
        assert_eq!(reaped(&scan, 60).0, None, "a kernel thread");
    }

    /// An `io` read failing while the process is still there: unknown counters, still listed.
    #[test]
    fn an_io_read_failing_for_a_live_process_writes_unknown_counters() {
        let (tree, mut scanner) = bash_and_tar("enoent", 10);
        tree.file("11/io", "ENOENT");
        let scan = scanner.scan().expect("scan");
        let tar = listed(&scan, 11);
        assert_eq!((tar.counters, tar.reaped), (None, None));
        assert_eq!(scan.reread, 0, "nothing vanished");
    }

    /// A 15-byte `comm` is replaced by the `exe` link's basename when that extends it (#1573); a
    /// script (its `exe` is the interpreter) and a shorter `comm` keep theirs.
    #[test]
    #[cfg(unix)]
    fn a_15_byte_comm_is_named_by_its_executable() {
        let tree = Tree::new("names");
        let link = |pid: u32, target: &str| {
            std::os::unix::fs::symlink(target, tree.0.join(format!("{pid}/exe"))).expect("link")
        };
        tree.process(70, "valheim_server.", 1, 5, 0);
        link(70, "/home/steam/valheim/valheim_server.x86_64");
        tree.process(71, "valheim-world-b", 1, 6, 0);
        link(71, "/usr/bin/bash");
        tree.process(72, "nginx", 1, 7, 0);
        link(72, "/usr/sbin/nginx-debug");
        tree.process(73, "systemd-journal", 1, 8, 0);
        link(73, "/usr/lib/systemd/systemd-journald (deleted)");
        // No readable link (the process is exiting): comm.
        tree.process(74, "dart_flutter_to", 1, 9, 0);
        let scan = Scanner::new(&tree.0).scan().expect("scan");
        let names: Vec<&str> = scan.entries.iter().map(|e| e.name.as_str()).collect();
        assert_eq!(
            names,
            [
                "valheim_server.x86_64",
                "valheim-world-b",
                "nginx",
                "systemd-journald",
                "dart_flutter_to"
            ]
        );
    }

    #[test]
    fn the_scan_lists_in_pid_order_with_names_flags_and_sigchld() {
        let tree = Tree::new("order");
        tree.process(300, "web server", 1, 9, 7);
        tree.process(2, "kthreadd", 0, 1, 0);
        tree.file(
            "2/stat",
            // sigignore (field 33) all ones, as the kernel shows kthreadd; sigcatch 0.
            "2 (kthreadd) S 0 0 0 0 -1 2129984 0 0 0 0 0 0 0 0 20 0 1 0 1 0 0 1 0 0 0 0 0 0 0 \
             2147483647 0 0 0\n",
        );
        tree.file(
            "self/stat",
            "1 (x) S 0 0 0 0 -1 0 0 0 0 0 0 0 0 0 0 0 0 0 1\n",
        );
        let scan = Scanner::new(&tree.0).scan().expect("scan");
        let pids: Vec<u32> = scan.entries.iter().map(|e| e.pid).collect();
        assert_eq!(pids, [2, 300]);
        assert!(scan.entries[0].kernel_thread && scan.entries[0].sigchld_ignored);
        assert_eq!(scan.entries[1].name, "web server");
        assert!(!scan.entries[1].kernel_thread && !scan.entries[1].sigchld_ignored);
        assert!(Scanner::new(tree.0.join("absent")).scan().is_err());
    }

    #[test]
    fn the_receiver_walks_up_through_vanished_parents() {
        // cron(20) → sh(21) → tar(22); sh and tar vanished.
        let previous: HashMap<u32, (u64, u32)> =
            [(20, (5, 1)), (21, (6, 20)), (22, (7, 21))].into();
        let lookup = |p: u32| previous.get(&p).copied();
        let alive = |p: u32, start: u64| p == 20 && start == 5;
        assert_eq!(receiver(22, lookup, alive, 3), Some(20));
        assert_eq!(receiver(21, lookup, alive, 3), Some(20));
        // A reused pid is not the parent: no receiver.
        assert_eq!(receiver(21, lookup, |p, s| p == 20 && s == 99, 3), None);
        // A parent the previous read never saw: none.
        assert_eq!(receiver(20, lookup, alive, 3), None);
    }
}
