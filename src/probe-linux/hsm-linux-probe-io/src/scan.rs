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
//! with unknown counters (the probe keeps its previous ones).

use std::collections::{HashMap, HashSet};
use std::io::ErrorKind;
use std::path::{Path, PathBuf};

use crate::procstat::{read_pid_stat, read_processes};
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
            scan.entries.push(Entry {
                pid: stat.pid,
                start_ticks: stat.start_ticks,
                ppid: stat.ppid,
                kernel_thread: stat.is_kernel_thread(),
                sigchld_ignored: stat.ignores_sigchld(),
                counters,
                comm: stat.comm,
            });
        }

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
            if let (Some(&i), Ok(Some(counters))) = (index.get(&to), self.read_counters(to)) {
                scan.entries[i].counters = Some(counters);
                scan.reread += 1;
            }
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

        /// A process: its `stat` (parent, start time, the ignored-signal mask) and its `io`.
        pub fn process(&self, pid: u32, comm: &str, ppid: u32, start: u64, write: u64) {
            self.file(
                &format!("{pid}/stat"),
                &format!(
                    "{pid} ({comm}) S {ppid} {pid} {pid} 0 -1 4194560 0 0 0 0 1 1 0 0 20 0 1 0 \
                     {start} 1 1 1 0 0 0 0 0 0 0 0 0 0 0\n"
                ),
            );
            self.file(&format!("{pid}/io"), &io_text(write));
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

    /// The kernel, scripted by what an `io` file holds:
    ///
    /// * `REAP <reaper> <write>` — reaped **between its `stat` and `io` reads**: the reaper's
    ///   counter becomes `<write>`, the pid disappears, `ENOENT`;
    /// * `THEN-REAP <child> <write>` on the first line — this (the reaper's) `io` is read first,
    ///   then `<child>` is reaped: the rest is returned, the reaper's counter becomes `<write>`;
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
            std::fs::write(root.join(n[0].to_string()).join("io"), io_text(n[1]))?;
            std::fs::remove_dir_all(dir)?;
            return Err(std::io::Error::from(ErrorKind::NotFound));
        }
        if let Some(rest) = text.strip_prefix("THEN-REAP ") {
            let (first, io) = rest.split_once('\n').expect("two parts");
            let n = numbers(first);
            std::fs::remove_dir_all(root.join(n[0].to_string()))?;
            std::fs::write(path, io_text(n[1]))?;
            return Ok(io.to_string());
        }
        Ok(text)
    }

    fn written(scan: &Scan, pid: u32) -> Option<u64> {
        scan.entries
            .iter()
            .find(|entry| entry.pid == pid)
            .and_then(|entry| entry.counters)
            .map(|counters| counters.write_bytes)
    }

    /// bash (`reaper`) and its child tar (11) at the first scan; tar then wrote 20 MB more and was
    /// reaped: bash's counter = its own 3 MB + tar's final 120 MB.
    fn bash_and_tar(tag: &str, reaper: u32) -> (Tree, Scanner) {
        let tree = Tree::new(tag);
        tree.process(reaper, "bash", 1, 5, MB);
        tree.process(11, "tar", reaper, 6, 100 * MB);
        let mut scanner = Scanner::with_reader(&tree.0, scripted);
        scanner.scan().expect("first scan");
        (tree, scanner)
    }

    const AFTER_REAP: u64 = 123 * MB;

    /// The reaper's `io` is read, then the child is reaped before its own `io` is read: the
    /// snapshot still carries the reaper's post-reap value (read again after the scan).
    #[test]
    fn a_child_reaped_right_after_its_parents_read_leaves_the_reread_value() {
        let (tree, mut scanner) = bash_and_tar("race-a", 10);
        tree.file(
            "10/io",
            &format!("THEN-REAP 11 {AFTER_REAP}\n{}", io_text(3 * MB)),
        );
        let scan = scanner.scan().expect("scan");
        assert_eq!(written(&scan, 10), Some(AFTER_REAP));
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
        assert!(scan.entries.iter().all(|entry| entry.pid != 11));
    }

    /// After a pid wrap the parent has the higher pid, so the scan reads the child first.
    #[test]
    fn a_parent_with_a_higher_pid_than_its_child_is_read_after_the_reap() {
        let (tree, mut scanner) = bash_and_tar("race-c", 900);
        tree.process(900, "bash", 1, 5, 3 * MB);
        tree.file("11/io", &format!("REAP 900 {AFTER_REAP}"));
        let scan = scanner.scan().expect("scan");
        assert_eq!(written(&scan, 900), Some(AFTER_REAP));
        assert_eq!(scan.entries.len(), 1);
    }

    /// An `io` read failing while the process is still there: unknown counters, still listed.
    #[test]
    fn an_io_read_failing_for_a_live_process_writes_unknown_counters() {
        let (tree, mut scanner) = bash_and_tar("enoent", 10);
        tree.file("11/io", "ENOENT");
        let scan = scanner.scan().expect("scan");
        let tar = scan.entries.iter().find(|e| e.pid == 11).expect("listed");
        assert_eq!(tar.counters, None);
        assert_eq!(scan.reread, 0, "nothing vanished");
    }

    #[test]
    fn the_scan_lists_in_pid_order_with_names_flags_and_sigchld() {
        let tree = Tree::new("order");
        tree.process(300, "web server", 1, 9, 7);
        tree.process(2, "kthreadd", 0, 1, 0);
        tree.file(
            "2/stat",
            "2 (kthreadd) S 0 0 0 0 -1 2129984 0 0 0 0 0 0 0 0 20 0 1 0 1 0 0 1 0 0 0 0 0 0 0 0 \
             2147483647 0 0\n",
        );
        tree.file(
            "self/stat",
            "1 (x) S 0 0 0 0 -1 0 0 0 0 0 0 0 0 0 0 0 0 0 1\n",
        );
        let scan = Scanner::new(&tree.0).scan().expect("scan");
        let pids: Vec<u32> = scan.entries.iter().map(|e| e.pid).collect();
        assert_eq!(pids, [2, 300]);
        assert!(scan.entries[0].kernel_thread && scan.entries[0].sigchld_ignored);
        assert_eq!(scan.entries[1].comm, "web server");
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
