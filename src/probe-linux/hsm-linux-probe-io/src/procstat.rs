//! `/proc/<pid>/stat` and the process table: read by the probe's top-CPU sampler (#1479) and by
//! this helper (#1547), so both see the same processes under the same names.
//!
//! The process name is field 2 of the stat line: the kernel renders it with the function it uses
//! for `<proc>/<pid>/comm` (`proc_task_name(…, escape = false)` in `fs/proc/array.c`), so both
//! carry the same bytes and a second read per process would add nothing. Like `comm`, it is
//! `task->comm`: at most **15 bytes** (`TASK_COMM_LEN - 1`), so a long executable name arrives
//! truncated (`systemd-journald` → `systemd-journal`).

use std::path::Path;

/// `PF_KTHREAD` (include/linux/sched.h): the task is a kernel thread.
pub const PF_KTHREAD: u64 = 0x0020_0000;

/// `SIGCHLD` (17) in a signal mask: bit 16.
pub const SIGCHLD_MASK: u64 = 1 << 16;

/// The fields of `/proc/<pid>/stat` the samplers use.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct PidStat {
    pub pid: u32,
    /// Field 2 without its parentheses (`task->comm`, ≤ 15 bytes, lossily decoded).
    pub comm: String,
    /// Field 4, the parent's pid (0 for the kernel's own roots, `init` and `kthreadd`): the top
    /// disk I/O reaper walk follows it ([`crate::scan::receiver`]).
    pub ppid: u32,
    /// Field 9, the `PF_*` flags.
    pub flags: u64,
    /// Fields 14 + 15, `utime + stime`, in clock ticks (USER_HZ). For a thread-group leader the
    /// kernel sums every thread, live and exited, so this is the whole process.
    pub cpu_ticks: u64,
    /// Field 22, the start time in clock ticks after boot: with the pid it identifies a process,
    /// so a reused pid is never credited with its predecessor's time.
    pub start_ticks: u64,
    /// Field 33, `sigignore`: the ignored signals (the low 31 bits; `SIGCHLD` is among them); 0 when absent.
    pub sigignore: u64,
}

impl PidStat {
    pub fn is_kernel_thread(&self) -> bool {
        self.flags & PF_KTHREAD != 0
    }

    /// The process ignores `SIGCHLD`: the kernel then reaps its children itself (`autoreap`) and
    /// does not add their I/O counters to it.
    pub fn ignores_sigchld(&self) -> bool {
        self.sigignore & SIGCHLD_MASK != 0
    }
}

/// Parse `/proc/<pid>/stat`. The name (field 2) is in parentheses and may itself contain spaces,
/// parentheses and `)` — a process can name itself anything — so the fields after it are split
/// at the **last** `)` (the same rule as the collector's `ParseProcSelfStat`). `None` for
/// anything malformed or short.
pub fn parse_pid_stat(text: &str) -> Option<PidStat> {
    let open = text.find('(')?;
    let close = text.rfind(')')?;
    if close < open {
        return None;
    }
    let pid = text[..open].trim().parse().ok()?;
    let comm = text[open + 1..close].to_string();
    // rest[0] is field 3 (state), so field N is rest[N - 3].
    let rest: Vec<&str> = text[close + 1..].split_whitespace().collect();
    let field = |n: usize| -> Option<u64> { rest.get(n - 3)?.parse().ok() };
    Some(PidStat {
        pid,
        comm,
        ppid: u32::try_from(field(4)?).ok()?,
        flags: field(9)?,
        cpu_ticks: field(14)?.checked_add(field(15)?)?,
        start_ticks: field(22)?,
        sigignore: field(33).unwrap_or(0),
    })
}

/// Read one `<proc>/<pid>/stat`; `None` when it is gone, malformed or names another pid.
pub fn read_pid_stat(proc_root: &Path, pid: u32) -> Option<PidStat> {
    let bytes = std::fs::read(proc_root.join(pid.to_string()).join("stat")).ok()?;
    parse_pid_stat(&String::from_utf8_lossy(&bytes)).filter(|stat| stat.pid == pid)
}

/// Every process in `<proc>`: one `<proc>/<pid>/stat` read per numeric entry (thread-group
/// leaders only; threads live under `<pid>/task`), in ascending pid order. A `<proc>/<pid>` that
/// vanishes between the listing and the read, or whose `stat` is malformed or names another pid,
/// is skipped silently; that is normal. Only an unlistable `<proc>` is an error.
pub fn read_processes(proc_root: &Path) -> Result<Vec<PidStat>, String> {
    let entries = std::fs::read_dir(proc_root)
        .map_err(|error| format!("cannot list {}: {error}", proc_root.display()))?;
    let mut pids: Vec<u32> = entries
        .flatten()
        .filter_map(|entry| entry.file_name().to_str()?.parse().ok())
        .collect();
    pids.sort_unstable();
    Ok(pids
        .into_iter()
        .filter_map(|pid| read_pid_stat(proc_root, pid))
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Fields 31..34 are `signal blocked sigignore sigcatch` (proc(5)). A shell, `cron`, `nginx` or
    /// any Go program CATCHES `SIGCHLD` (field 34) and reaps with `wait` — its children's counters
    /// are added to it; only one that IGNORES it (field 33) autoreaps.
    #[test]
    fn sigignore_is_field_33_not_sigcatch() {
        let short = "7 (cron) S 1 7 7 0 -1 4194560 1 0 0 0 1 2 0 0 20 0 1 0 99 1 1 1";
        assert_eq!(parse_pid_stat(short).expect("short").sigignore, 0);

        // bash as the kernel renders it: blocked 65536, sigignore 3670020, sigcatch 1266777851
        // (SIGCHLD caught, not ignored).
        let bash = "4242 (bash) S 4241 4242 4242 34816 4250 4194560 1480 6812 0 0 3 2 9 6 20 0 1 \
                    0 9120 8970240 1312 18446744073709551615 94 95 140 0 0 0 65536 3670020 \
                    1266777851 1 0 0 17 2 0 0 0 0 0 96 97 98 140 141 141 141 0";
        let stat = parse_pid_stat(bash).expect("bash");
        assert_eq!((stat.start_ticks, stat.sigignore), (9120, 3_670_020));
        assert!(
            !stat.ignores_sigchld(),
            "catching SIGCHLD is not ignoring it"
        );

        // A daemon with SIGCHLD = SIG_IGN: bit 16 in sigignore, nothing caught.
        let autoreap = "500 (daemon) S 1 500 500 0 -1 4194624 9 0 0 0 1 1 0 0 20 0 1 0 77 1 1 \
                        18446744073709551615 1 1 1 0 0 0 0 65536 0 0 0 0 17 0 0 0 0 0 0";
        let stat = parse_pid_stat(autoreap).expect("daemon");
        assert_eq!(stat.sigignore, SIGCHLD_MASK);
        assert!(stat.ignores_sigchld());
    }
}
