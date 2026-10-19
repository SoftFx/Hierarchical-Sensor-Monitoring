//! `/proc/<pid>/stat` and the process table: read by the probe's top-CPU sampler (#1479) and by
//! this helper (#1547), so both see the same processes under the same names.
//!
//! The process name is field 2 of the stat line: the kernel renders it with the function it uses
//! for `<proc>/<pid>/comm` (`proc_task_name(…, escape = false)` in `fs/proc/array.c`), so both
//! carry the same bytes and a second read per process would add nothing. Like `comm`, it is
//! `task->comm`: at most **15 bytes** (`TASK_COMM_LEN - 1`), so a long executable name arrives
//! truncated (`systemd-journald` → `systemd-journal`). [`extended_name`] is the one rule both sides
//! use to recover the full name (#1573): the helper from the `exe` link, the probe from argv[0].

use std::path::Path;

/// `PF_KTHREAD` (include/linux/sched.h): the task is a kernel thread.
pub const PF_KTHREAD: u64 = 0x0020_0000;

/// `SIGCHLD` (17) in a signal mask: bit 16.
pub const SIGCHLD_MASK: u64 = 1 << 16;

/// `TASK_COMM_LEN - 1`: the longest `comm`. Only a name of exactly this length can be truncated.
pub const COMM_LEN: usize = 15;

/// `NAME_MAX`: the longest file name. No executable's basename is longer, so a longer candidate
/// (an argv[0] any local user can make up, up to the 4 KiB read) is never a full name — it would
/// otherwise become a sensor path segment of that length.
pub const NAME_MAX: usize = 255;

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
    /// Field 11, `cminflt`: the minor faults of the children the process has reaped. The kernel
    /// adds a child's faults and its I/O counters to the reaper in one step (`wait_task_zombie`)
    /// and a child that ran has faulted at least once, so this growing says "it reaped a child" —
    /// which tells reaped children's bytes from exited threads' (#1572).
    pub cminflt: u64,
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
        cminflt: field(11)?,
        cpu_ticks: field(14)?.checked_add(field(15)?)?,
        start_ticks: field(22)?,
        sigignore: field(33).unwrap_or(0),
    })
}

/// Whether `comm` — as decoded from the stat line, lossily — may be the kernel's cut of a longer
/// name: 15 raw bytes decode to at least 15 bytes (exactly 15 when valid UTF-8; more when the cut
/// split a multibyte character, which decodes to U+FFFD), so anything shorter was never cut. Only
/// such a `comm` is looked at again (the `exe` link, argv[0]).
pub fn may_be_cut(comm: &str) -> bool {
    comm.len() >= COMM_LEN
}

/// `bytes` decoded as the kernel's `comm` would be after the stat line's lossy decoding.
fn as_comm(bytes: &[u8]) -> std::borrow::Cow<'_, str> {
    String::from_utf8_lossy(bytes)
}

/// The full name of a process whose `comm` may have been cut at [`COMM_LEN`] bytes (#1573), from
/// `candidate` — the target of its `exe` link (the helper, which may read it) or its argv[0] (the
/// probe, which usually may not): the candidate's basename, without the ` (deleted)` the kernel
/// appends to a replaced executable. `None` — keep `comm` — unless the basename is longer than
/// [`COMM_LEN`] bytes, **its first [`COMM_LEN`] bytes are `comm`** (compared as bytes, decoded the
/// way the stat line is, so a cut inside a multibyte character matches too; the kernel's `comm` is
/// the first 15 bytes of the executed file's basename, so anything else names another file: a
/// script's interpreter, a symlink's differently named target, a rewritten title), it holds no
/// U+FFFD (a name that is not valid UTF-8 is never taken, so an extended name never holds one —
/// what [`original_comm`] relies on), it is at most [`NAME_MAX`] bytes (no real file name is
/// longer; an argv[0] can be anything) and it is no interpreter ([`is_interpreter`]). Callers pass
/// argv[0] only, never an argument: arguments can carry secrets.
pub fn extended_name(comm: &str, candidate: &str) -> Option<String> {
    if !may_be_cut(comm) {
        return None;
    }
    let candidate = candidate.strip_suffix(" (deleted)").unwrap_or(candidate);
    let base = candidate.rsplit('/').next().unwrap_or(candidate);
    let bytes = base.as_bytes();
    (bytes.len() > COMM_LEN
        && bytes.len() <= NAME_MAX
        && as_comm(&bytes[..COMM_LEN]) == comm
        && !base.contains(char::REPLACEMENT_CHARACTER)
        && !is_interpreter(base))
    .then(|| base.to_string())
}

/// An interpreter's basename (`bash`, `python3.12`, `node`, `java`, …): a script it runs has its
/// own name in argv[1], which is never read, so such a process keeps its `comm`.
pub fn is_interpreter(base: &str) -> bool {
    const EXACT: &[&str] = &[
        "sh", "bash", "dash", "ash", "zsh", "ksh", "mksh", "fish", "busybox", "env", "node",
        "nodejs", "deno", "bun", "java", "dotnet", "mono", "Rscript", "wish", "awk", "gawk",
        "mawk",
    ];
    const VERSIONED: &[&str] = &["python", "perl", "ruby", "php", "lua", "tclsh", "pwsh"];
    EXACT.contains(&base)
        || VERSIONED.iter().any(|prefix| {
            base.strip_prefix(prefix).is_some_and(|rest| {
                rest.chars()
                    .all(|c| c.is_ascii_digit() || matches!(c, '.' | '-'))
            })
        })
}

/// The `comm` a user process's `name` (as the helper writes it) was extended from by
/// [`extended_name`]: its first [`COMM_LEN`] bytes, decoded as the stat line is. `None` when `name`
/// is a `comm` itself — at most [`COMM_LEN`] bytes, or longer only because the kernel's cut split a
/// multibyte character and decoding made it U+FFFD. An extended name never holds U+FFFD and a
/// `comm` longer than [`COMM_LEN`] bytes always does, so this tells them apart exactly.
pub fn original_comm(name: &str) -> Option<String> {
    let bytes = name.as_bytes();
    (bytes.len() > COMM_LEN && !name.contains(char::REPLACEMENT_CHARACTER))
        .then(|| as_comm(&bytes[..COMM_LEN]).into_owned())
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

    /// Field 10 is `minflt`, 11 `cminflt` (the reaped children's), 12 `majflt`, 13 `cmajflt`.
    #[test]
    fn cminflt_is_field_11() {
        let line = "4242 (bash) S 4241 4242 4242 34816 4250 4194560 693 488 0 7 3 2 9 6 20 0 1 0 \
                    9120 8970240 1312 18446744073709551615 94 95 140 0 0 0 65536 3670020";
        assert_eq!(parse_pid_stat(line).expect("bash").cminflt, 488);
    }

    /// Only a 15-byte `comm` is extended, only by a basename it starts, never to an interpreter.
    #[test]
    fn a_15_byte_comm_is_extended_only_by_a_name_it_starts() {
        let valheim = "valheim_server.";
        assert_eq!(valheim.len(), COMM_LEN);
        for candidate in [
            "./valheim_server.x86_64",
            "valheim_server.x86_64",
            "/home/steam/valheim/valheim_server.x86_64",
            "/home/steam/valheim/valheim_server.x86_64 (deleted)",
        ] {
            assert_eq!(
                extended_name(valheim, candidate).as_deref(),
                Some("valheim_server.x86_64"),
                "{candidate}"
            );
        }
        assert_eq!(
            extended_name("systemd-journal", "/usr/lib/systemd/systemd-journald").as_deref(),
            Some("systemd-journald")
        );
        // A script: comm is the script's name, exe and argv[0] are its interpreter.
        assert_eq!(extended_name("valheim-world-b", "/usr/bin/bash"), None);
        // Not longer, not a prefix (another file), a comm shorter than 15 bytes, nothing.
        assert_eq!(extended_name(valheim, "valheim_server."), None);
        assert_eq!(extended_name(valheim, "/opt/other_binary_name"), None);
        assert_eq!(
            extended_name("dart_flutter_to", "/usr/lib/dart/bin/dart"),
            None
        );
        assert_eq!(extended_name("nginx", "/usr/sbin/nginx-debug"), None);
        assert_eq!(extended_name(valheim, ""), None);
        // An interpreter whose name happens to extend the comm still keeps it.
        assert_eq!(extended_name("python3.12-3.12", "python3.12-3.12.1"), None);
        for interpreter in [
            "bash",
            "sh",
            "python3",
            "python3.12",
            "perl5.36",
            "node",
            "pwsh",
        ] {
            assert!(is_interpreter(interpreter), "{interpreter}");
        }
        for binary in ["valheim_server.x86_64", "pythonista", "nodes", "bashtop"] {
            assert!(!is_interpreter(binary), "{binary}");
        }
        assert_eq!(
            original_comm("valheim_server.x86_64").as_deref(),
            Some(valheim)
        );
        assert_eq!(original_comm(valheim), None);
    }

    /// The kernel cuts at 15 **bytes**, so a cut can split a multibyte character; the stat line's
    /// lossy decoding then ends the `comm` in U+FFFD and makes it longer than 15 bytes. Such a
    /// `comm` is matched byte-wise (still only by a name it is the cut of), and is never mistaken
    /// for an extended name.
    #[test]
    fn a_comm_cut_inside_a_multibyte_character_is_matched_byte_wise() {
        let full = "sauvegarde_bdd\u{e9}-nuit"; // 14 ASCII bytes, then "é" (C3 A9): cut inside it
        let raw = &full.as_bytes()[..COMM_LEN];
        let comm = String::from_utf8_lossy(raw).into_owned();
        assert_eq!(comm, "sauvegarde_bdd\u{fffd}");
        assert!(comm.len() > COMM_LEN && may_be_cut(&comm));
        assert_eq!(
            extended_name(&comm, &format!("/opt/backup/{full}")).as_deref(),
            Some(full)
        );
        assert_eq!(original_comm(full).as_deref(), Some(comm.as_str()));
        // Not a prefix (byte-wise): another file. A shorter comm is never looked at.
        assert_eq!(extended_name(&comm, "/opt/sauvegarde_bddX-nuit"), None);
        assert!(!may_be_cut("nginx"));
        // The lossily decoded comm itself — at the end or in the middle — is no extended name.
        assert_eq!(original_comm(&comm), None);
        let middle = String::from_utf8_lossy(b"a\xe9bcdefghijklmn").into_owned();
        assert!(middle.len() > COMM_LEN);
        assert_eq!(original_comm(&middle), None);
        // A candidate that is not valid UTF-8 (decoded with U+FFFD) is never taken.
        assert_eq!(extended_name(&comm, "sauvegarde_bdd\u{fffd}-nuit"), None);
    }

    /// A basename longer than `NAME_MAX` names no real file: an argv[0] like that (any local user
    /// can start `./<4 KB name>`) keeps the `comm`, so it cannot make a 4 KB sensor segment.
    #[test]
    fn a_name_longer_than_name_max_is_never_taken() {
        let comm = "valheim_server.";
        let longest = format!("{comm}{}", "x".repeat(NAME_MAX - COMM_LEN));
        assert_eq!(longest.len(), NAME_MAX);
        assert_eq!(
            extended_name(comm, &format!("./{longest}")).as_deref(),
            Some(longest.as_str())
        );
        let too_long = format!("{comm}{}", "x".repeat(300 - COMM_LEN));
        assert_eq!(too_long.len(), 300);
        assert_eq!(extended_name(comm, &format!("./{too_long}")), None);
        assert_eq!(
            extended_name(comm, &format!("/opt/{too_long} (deleted)")),
            None
        );
    }
}
