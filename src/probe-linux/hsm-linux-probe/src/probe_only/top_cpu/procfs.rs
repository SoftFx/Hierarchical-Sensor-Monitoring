//! Per-process CPU sampling from `/proc` (#1479): the parsing, the delta math, the name rule and
//! the top-N selection. Pure over an injectable proc root, so every rule is tested on a fixture
//! tree; nothing here touches the collector.
//!
//! # What one sample reads
//!
//! `<proc>/stat` (the host's CPU time) and, for every numeric entry of `<proc>`, `<proc>/<pid>/stat`
//! — one small read per process, nothing outside `/proc`. The process name is field 2 of that same
//! file: the kernel renders it with the function it uses for `<proc>/<pid>/comm`
//! (`proc_task_name(…, escape = false)` in `fs/proc/array.c`), so both carry the same bytes and a
//! second read per process would add nothing. Like `comm`, it is `task->comm`: at most **15
//! bytes** (`TASK_COMM_LEN - 1`), so a long executable name arrives truncated
//! (`systemd-journald` → `systemd-journal`).

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, PathBuf};

/// `PF_KTHREAD` (include/linux/sched.h): the task is a kernel thread.
const PF_KTHREAD: u64 = 0x0020_0000;

/// The fields of `/proc/<pid>/stat` the sampler uses.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct PidStat {
    pub pid: u32,
    /// Field 2 without its parentheses (`task->comm`, ≤ 15 bytes, lossily decoded).
    pub comm: String,
    /// Field 9, the `PF_*` flags.
    pub flags: u64,
    /// Fields 14 + 15, `utime + stime`, in clock ticks (USER_HZ). For a thread-group leader the
    /// kernel sums every thread, live and exited, so this is the whole process.
    pub cpu_ticks: u64,
    /// Field 22, the start time in clock ticks after boot: with the pid it identifies a process,
    /// so a reused pid is never credited with its predecessor's time.
    pub start_ticks: u64,
}

impl PidStat {
    pub fn is_kernel_thread(&self) -> bool {
        self.flags & PF_KTHREAD != 0
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
        flags: field(9)?,
        cpu_ticks: field(14)?.checked_add(field(15)?)?,
        start_ticks: field(22)?,
    })
}

/// The host's total CPU time from the aggregate `cpu` line of `/proc/stat`, in clock ticks:
/// every field except `guest`/`guest_nice` (already counted in `user`/`nice`). The same total the
/// collector's `Total CPU` divides by (`ParseProcStatCpuTimes` in `src/native/collector`), so a
/// process percentage here is a share of the same whole as that sensor — the whole host, all
/// cores = 100 %, as on Windows.
pub fn parse_total_ticks(text: &str) -> Option<u64> {
    const IDLE: usize = 4;
    const GUEST: usize = 9;
    const GUEST_NICE: usize = 10;
    let fields: Vec<&str> = text.lines().next()?.split_whitespace().collect();
    // The aggregate line is exactly "cpu"; "cpu0", "cpu1", … are the per-core lines.
    if fields.len() <= IDLE || fields[0] != "cpu" {
        return None;
    }
    let mut total: u64 = 0;
    for (index, field) in fields.iter().enumerate().skip(1) {
        let value: u64 = field.parse().ok()?;
        if index != GUEST && index != GUEST_NICE {
            total = total.checked_add(value)?;
        }
    }
    Some(total)
}

/// The characters a sensor name keeps: exactly those the server's alert-template wildcard `*`
/// matches (`AllValidSymbols` in `HSMServer.Core/PathTemplates/PathTemplateConverter.cs`:
/// letters, digits, space, `. _ # , % $ - &`), ASCII only. Anything else becomes `_`, so every
/// name is a single path segment (no `/`, the separator; no `\` or tab, which the server rejects)
/// and a template written for the Windows agents (`…/Top CPU processes/*`) matches every Linux
/// sensor too. Windows needs no such rule: an image name cannot hold `/ \ : * ? " < > |`.
fn is_kept(c: char) -> bool {
    c.is_ascii_alphanumeric() || matches!(c, ' ' | '.' | '_' | '#' | ',' | '%' | '$' | '-' | '&')
}

/// The sensor name of a process: its `comm`, normalized (see [`is_kept`]) and trimmed like the
/// server trims path segments. A kernel thread is named by the part before its first `/`, so the
/// per-CPU and per-device instances are one sensor, summed like several `chrome.exe`s on Windows:
/// `kworker/0:1-events`, `kworker/u8:2-flush-8:0` → `kworker`; `ksoftirqd/3` → `ksoftirqd`;
/// `irq/42-nvme0q1` → `irq`; `jbd2/sda1-8` → `jbd2`. A user process keeps its whole name (a `/`
/// in it becomes `_`). `None` for a name with nothing left: such a process has no sensor, like an
/// empty image name on Windows.
pub fn sensor_name(comm: &str, kernel_thread: bool) -> Option<String> {
    let base = if kernel_thread {
        comm.split('/').next().unwrap_or(comm)
    } else {
        comm
    };
    let normalized: String = base
        .chars()
        .map(|c| if is_kept(c) { c } else { '_' })
        .collect();
    let trimmed = normalized.trim();
    if trimmed.is_empty() {
        None
    } else {
        Some(trimmed.to_string())
    }
}

/// One name's CPU over the last interval.
#[derive(Clone, Debug, PartialEq)]
pub struct Usage {
    pub name: String,
    /// Percent of the whole host (all cores = 100 %), summed over every process of this name.
    pub percent: f64,
    /// The process of this name that used the most CPU in the interval — whose executable the
    /// description names when the sensor is created.
    pub pid: u32,
    pub kernel_thread: bool,
}

/// What one sample saw, besides the usage.
#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct Visibility {
    /// Processes whose `stat` was read.
    pub processes: usize,
}

/// Samples per-process CPU between successive calls. The first call only sets the baseline.
pub struct Sampler {
    proc_root: PathBuf,
    /// (pid) → (start ticks, cpu ticks) of the previous sample.
    previous: HashMap<u32, (u64, u64)>,
    previous_total: Option<u64>,
}

impl Sampler {
    pub fn new(proc_root: impl Into<PathBuf>) -> Self {
        Self {
            proc_root: proc_root.into(),
            previous: HashMap::new(),
            previous_total: None,
        }
    }

    pub fn proc_root(&self) -> &Path {
        &self.proc_root
    }

    /// Read the process table and return every name that used CPU since the previous call
    /// (percent > 0), or why nothing could be measured. A process present in only one of the two
    /// samples — started or exited in between — or whose pid now belongs to another process
    /// (different start time) contributes nothing: its time cannot be attributed to the interval.
    /// A `<proc>/<pid>` that vanishes while being read is skipped silently; that is normal.
    ///
    /// When `/proc/stat` or the `/proc` listing cannot be read, the previous baseline is kept, so
    /// the next successful sample measures the longer interval correctly.
    pub fn sample(&mut self) -> Result<(BTreeMap<String, Usage>, Visibility), String> {
        let stat_path = self.proc_root.join("stat");
        let total_text = std::fs::read_to_string(&stat_path)
            .map_err(|error| format!("cannot read {}: {error}", stat_path.display()))?;
        let total = parse_total_ticks(&total_text)
            .ok_or_else(|| format!("{} has no aggregate cpu line", stat_path.display()))?;
        let entries = std::fs::read_dir(&self.proc_root)
            .map_err(|error| format!("cannot list {}: {error}", self.proc_root.display()))?;

        let mut current: HashMap<u32, (u64, u64)> = HashMap::new();
        let mut processes: Vec<PidStat> = Vec::new();
        for entry in entries.flatten() {
            let file_name = entry.file_name();
            let Some(pid) = file_name.to_str().and_then(|name| name.parse::<u32>().ok()) else {
                continue;
            };
            // Gone between the listing and the read, or a pid directory being torn down: normal.
            let Ok(bytes) = std::fs::read(entry.path().join("stat")) else {
                continue;
            };
            let Some(stat) = parse_pid_stat(&String::from_utf8_lossy(&bytes)) else {
                continue;
            };
            if stat.pid != pid {
                continue;
            }
            current.insert(pid, (stat.start_ticks, stat.cpu_ticks));
            processes.push(stat);
        }
        let visibility = Visibility {
            processes: processes.len(),
        };

        let previous = std::mem::replace(&mut self.previous, current);
        let previous_total = self.previous_total.replace(total);
        let mut by_name: BTreeMap<String, Usage> = BTreeMap::new();
        // No baseline yet, no time elapsed, or the total went backwards (a CPU taken offline
        // drops its ticks from the aggregate line): nothing to measure this time.
        let Some(total_delta) = previous_total
            .and_then(|before| total.checked_sub(before))
            .filter(|delta| *delta > 0)
        else {
            return Ok((by_name, visibility));
        };

        // Busiest process of each name first, so the representative pid is the busiest one.
        let mut shares: Vec<(f64, PidStat)> = Vec::new();
        for stat in processes {
            let Some(&(start, cpu_before)) = previous.get(&stat.pid) else {
                continue;
            };
            if start != stat.start_ticks || stat.cpu_ticks < cpu_before {
                continue;
            }
            let percent = (stat.cpu_ticks - cpu_before) as f64 / total_delta as f64 * 100.0;
            // Only a positive share counts (the managed `if (percent > 0)`), so minPercent 0
            // means "any process that used CPU", never "every process".
            if percent > 0.0 {
                shares.push((percent, stat));
            }
        }
        shares.sort_by(|a, b| b.0.total_cmp(&a.0).then(a.1.pid.cmp(&b.1.pid)));
        for (percent, stat) in shares {
            let kernel_thread = stat.is_kernel_thread();
            let Some(name) = sensor_name(&stat.comm, kernel_thread) else {
                continue;
            };
            by_name
                .entry(name.clone())
                .and_modify(|usage| usage.percent += percent)
                .or_insert(Usage {
                    name,
                    percent,
                    pid: stat.pid,
                    kernel_thread,
                });
        }
        Ok((by_name, visibility))
    }
}

/// The names at or above `min_percent`, busiest first, at most `count` — the Windows rule
/// (`SelectTopN` in `src/native/collector/src/cpu_top.cpp`, `WindowsTopCpuMonitor.Sample`): ties
/// broken by name ascending (ordinal), so equal shares order stably.
pub fn select_top(by_name: &BTreeMap<String, Usage>, count: usize, min_percent: f64) -> Vec<Usage> {
    let mut top: Vec<Usage> = by_name
        .values()
        .filter(|usage| usage.percent >= min_percent)
        .cloned()
        .collect();
    top.sort_by(|a, b| {
        b.percent
            .total_cmp(&a.percent)
            .then_with(|| a.name.cmp(&b.name))
    });
    top.truncate(count);
    top
}

/// Whether `<proc>` is mounted with `hidepid` (a `hidepid=` super option other than `0`/`off`),
/// from `mountinfo` text: the probe then sees only the processes of its own user. systemd's
/// `ProtectProc=invisible` (in the probe's unit) is exactly such a mount. Returns the option.
pub fn hidepid_option(mountinfo: &str, proc_mount_point: &str) -> Option<String> {
    let mut visible = None;
    for line in mountinfo.lines() {
        let Some((before, after)) = line.split_once(" - ") else {
            continue;
        };
        let mount_point = before.split_whitespace().nth(4);
        let mut after = after.split_whitespace();
        let (fs_type, super_options) = (after.next(), after.nth(1));
        if fs_type != Some("proc") || mount_point != Some(proc_mount_point) {
            continue;
        }
        // Mounts are listed in mount order: the last proc mount on that point is the visible one.
        visible = Some(
            super_options
                .into_iter()
                .flat_map(|options| options.split(','))
                .find_map(|option| option.strip_prefix("hidepid="))
                .filter(|value| !matches!(*value, "0" | "off"))
                .map(|value| format!("hidepid={value}")),
        );
    }
    visible.flatten()
}

#[cfg(test)]
pub mod tests {
    use super::*;
    use crate::probe_only::host::tests::FakeTree;

    /// A `/proc/<pid>/stat` line with the given name, flags, utime, stime and start time (the
    /// other fields are realistic filler).
    pub fn stat_line(
        pid: u32,
        comm: &str,
        flags: u64,
        utime: u64,
        stime: u64,
        start: u64,
    ) -> String {
        format!(
            "{pid} ({comm}) S 1 {pid} {pid} 0 -1 {flags} 120 0 0 0 {utime} {stime} 0 0 20 0 1 0 \
             {start} 12345678 345 18446744073709551615 1 1 0 0 0 0 0 4096 0 0 0 0 17 2 0 0 0 0 0\n"
        )
    }

    /// A fake `/proc` with a total of `total` ticks.
    pub fn fake_proc(tree: &FakeTree, total: u64) {
        // user nice system idle iowait irq softirq steal guest guest_nice: guest fields must not
        // count, so they carry a large value that would show up if they did.
        let user = total / 4;
        let idle = total - user;
        tree.file(
            "stat",
            &format!(
                "cpu  {user} 0 0 {idle} 0 0 0 0 999999 999999\ncpu0 {user} 0 0 {idle} 0 0 0 0 0 0\n"
            ),
        );
    }

    pub fn process(tree: &FakeTree, pid: u32, comm: &str, flags: u64, cpu: u64, start: u64) {
        tree.file(
            &format!("{pid}/stat"),
            &stat_line(pid, comm, flags, cpu, 0, start),
        );
    }

    #[test]
    fn stat_is_split_at_the_last_parenthesis_whatever_the_name_holds() {
        for comm in [
            "bash",
            "Web Content",
            "a) b (c",
            ") ) )",
            "(sd-pam)",
            "x) S 1 1 1 0 -1 999",
            "",
        ] {
            let parsed = parse_pid_stat(&stat_line(4242, comm, 0x40_0100, 700, 55, 9000))
                .unwrap_or_else(|| panic!("{comm:?} must parse"));
            assert_eq!(parsed.comm, comm);
            assert_eq!(parsed.pid, 4242);
            assert_eq!(parsed.flags, 0x40_0100);
            assert_eq!(parsed.cpu_ticks, 755);
            assert_eq!(parsed.start_ticks, 9000);
        }
    }

    #[test]
    fn a_real_kernel_line_parses() {
        // A kworker in the layout of a 6.x kernel: the flags carry PF_KTHREAD, utime is 0.
        let line = "87 (kworker/3:1-events) I 2 0 0 0 -1 69238880 0 0 0 0 0 112 0 0 20 0 1 0 \
                    112 0 0 18446744073709551615 0 0 0 0 0 0 0 2147483647 0 0 0 0 17 3 0 0 0 0 \
                    0 0 0 0 0 0 0 0 0\n";
        let parsed = parse_pid_stat(line).expect("parse");
        assert_eq!(parsed.comm, "kworker/3:1-events");
        assert!(parsed.is_kernel_thread());
        assert_eq!(parsed.cpu_ticks, 112);
        assert_eq!(parsed.start_ticks, 112);
        let user = parse_pid_stat(&stat_line(1, "systemd", 0x40_0100, 1, 1, 1)).expect("parse");
        assert!(!user.is_kernel_thread());
    }

    #[test]
    fn malformed_stat_is_rejected_not_guessed() {
        for bad in [
            "",
            "4242",
            "4242 (bash",
            "4242 bash) S 1",
            ")x( S",
            "x (bash) S 1 1 1 0 -1 0 0 0 0 0 1 1 0 0 20 0 1 0 5",
            "4242 (bash) S 1 1 1 0 -1 0 0 0 0 0 1 1 0 0 20 0 1 0",
            "4242 (bash) S 1 1 1 0 -1 0 0 0 0 0 x 1 0 0 20 0 1 0 5",
        ] {
            assert_eq!(parse_pid_stat(bad), None, "{bad:?}");
        }
    }

    #[test]
    fn the_total_excludes_guest_time_and_needs_the_aggregate_line() {
        assert_eq!(
            parse_total_ticks("cpu  10 20 30 40 50 60 70 80 1000 2000\ncpu0 1 1 1 1\n"),
            Some(360)
        );
        // An old kernel without steal/guest columns.
        assert_eq!(parse_total_ticks("cpu 1 2 3 4\n"), Some(10));
        assert_eq!(parse_total_ticks("cpu0 1 2 3 4 5\n"), None);
        assert_eq!(parse_total_ticks("cpu 1 2 3\n"), None);
        assert_eq!(parse_total_ticks("cpu 1 2 x 4\n"), None);
        assert_eq!(parse_total_ticks(""), None);
    }

    #[test]
    fn names_are_normalized_to_one_template_matchable_segment() {
        assert_eq!(sensor_name("nginx", false).as_deref(), Some("nginx"));
        assert_eq!(
            sensor_name("Web Content", false).as_deref(),
            Some("Web Content")
        );
        assert_eq!(sensor_name("a/b\\c\td", false).as_deref(), Some("a_b_c_d"));
        assert_eq!(sensor_name("(sd-pam)", false).as_deref(), Some("_sd-pam_"));
        assert_eq!(sensor_name("x:y*z?", false).as_deref(), Some("x_y_z_"));
        assert_eq!(sensor_name("  top  ", false).as_deref(), Some("top"));
        assert_eq!(sensor_name("naïve", false).as_deref(), Some("na_ve"));
        assert_eq!(
            sensor_name("v1.2_#,%$-&", false).as_deref(),
            Some("v1.2_#,%$-&")
        );
        assert_eq!(sensor_name("", false), None);
        assert_eq!(sensor_name("   ", false), None);
        // Kernel threads: one sensor per kind, the per-CPU/per-device suffix dropped.
        assert_eq!(
            sensor_name("kworker/0:1-events", true).as_deref(),
            Some("kworker")
        );
        assert_eq!(
            sensor_name("kworker/u8:2-flush-8:0", true).as_deref(),
            Some("kworker")
        );
        assert_eq!(
            sensor_name("ksoftirqd/3", true).as_deref(),
            Some("ksoftirqd")
        );
        assert_eq!(sensor_name("irq/42-nvme0q1", true).as_deref(), Some("irq"));
        assert_eq!(sensor_name("kswapd0", true).as_deref(), Some("kswapd0"));
        assert_eq!(sensor_name("/x", true), None);
        // The same text in a user process keeps its suffix.
        assert_eq!(
            sensor_name("kworker/0:1", false).as_deref(),
            Some("kworker_0_1")
        );

        // Every name the rule produces is matched by the server's template wildcard.
        for comm in ["a/b", "x:y", "(sd-pam)", "naïve", "k\u{7f}"] {
            let name = sensor_name(comm, false).expect("name");
            assert!(name.chars().all(is_kept), "{name}");
            assert!(!name.contains('/') && !name.contains('\\') && !name.contains('\t'));
        }
    }

    #[test]
    fn the_first_sample_is_a_baseline_then_deltas_are_shares_of_the_whole_host() {
        let tree = FakeTree::new("topcpu-delta");
        fake_proc(&tree, 10_000);
        process(&tree, 100, "postgres", 0, 1_000, 50);
        process(&tree, 200, "nginx", 0, 500, 60);
        let mut sampler = Sampler::new(&tree.0);
        let (first, seen) = sampler.sample().expect("sample");
        assert!(first.is_empty(), "the first sample is only the baseline");
        assert_eq!(seen.processes, 2);

        // 4 cores for 60 s at USER_HZ 100 = 24 000 ticks.
        fake_proc(&tree, 34_000);
        process(&tree, 100, "postgres", 0, 1_000 + 6_000, 50); // a quarter of the host
        process(&tree, 200, "nginx", 0, 500 + 24, 60); // 0.1 %
        let (usage, _) = sampler.sample().expect("sample");
        assert_eq!(usage["postgres"].percent, 25.0);
        assert!((usage["nginx"].percent - 0.1).abs() < 1e-9);
        assert_eq!(usage["postgres"].pid, 100);
    }

    #[test]
    fn processes_of_one_name_are_summed() {
        let tree = FakeTree::new("topcpu-sum");
        fake_proc(&tree, 0);
        for (pid, cpu) in [(10, 0), (11, 0), (12, 0)] {
            process(&tree, pid, "php-fpm8.2", 0, cpu, pid as u64);
        }
        process(&tree, 20, "kworker/0:1-events", PF_KTHREAD, 0, 1);
        process(&tree, 21, "kworker/1:2-mm_percpu_wq", PF_KTHREAD, 0, 1);
        let mut sampler = Sampler::new(&tree.0);
        sampler.sample().expect("baseline");

        fake_proc(&tree, 1_000);
        process(&tree, 10, "php-fpm8.2", 0, 100, 10);
        process(&tree, 11, "php-fpm8.2", 0, 300, 11);
        process(&tree, 12, "php-fpm8.2", 0, 0, 12); // idle: counts for nothing
        process(&tree, 20, "kworker/0:1-events", PF_KTHREAD, 20, 1);
        process(&tree, 21, "kworker/1:2-mm_percpu_wq", PF_KTHREAD, 30, 1);
        let (usage, _) = sampler.sample().expect("sample");
        assert_eq!(usage.len(), 2, "{usage:?}");
        assert_eq!(usage["php-fpm8.2"].percent, 40.0);
        assert_eq!(usage["php-fpm8.2"].pid, 11, "the busiest instance");
        assert_eq!(usage["kworker"].percent, 5.0);
        assert!(usage["kworker"].kernel_thread);
    }

    #[test]
    fn a_reused_pid_or_a_new_or_vanished_process_is_not_measured() {
        let tree = FakeTree::new("topcpu-reuse");
        fake_proc(&tree, 0);
        process(&tree, 300, "old", 0, 5_000, 100);
        process(&tree, 301, "exits", 0, 100, 101);
        let mut sampler = Sampler::new(&tree.0);
        sampler.sample().expect("baseline");

        fake_proc(&tree, 1_000);
        // pid 300 now belongs to another process: its 5 400 ticks are not a delta of 400.
        process(&tree, 300, "new", 0, 5_400, 900);
        // pid 301 exited; pid 302 started during the interval.
        std::fs::remove_dir_all(tree.0.join("301")).expect("rm");
        process(&tree, 302, "started", 0, 50, 950);
        let (usage, seen) = sampler.sample().expect("sample");
        assert!(usage.is_empty(), "{usage:?}");
        assert_eq!(seen.processes, 2);

        // From the next interval on, both are measured.
        fake_proc(&tree, 2_000);
        process(&tree, 300, "new", 0, 5_500, 900);
        process(&tree, 302, "started", 0, 70, 950);
        let (usage, _) = sampler.sample().expect("sample");
        assert_eq!(usage["new"].percent, 10.0);
        assert_eq!(usage["started"].percent, 2.0);
    }

    #[test]
    fn a_process_vanishing_mid_read_is_skipped_silently() {
        let tree = FakeTree::new("topcpu-vanish");
        fake_proc(&tree, 0);
        process(&tree, 1, "init", 0, 0, 1);
        // A pid directory without a stat (torn down between the listing and the read), one with
        // garbage, a non-numeric entry and a mismatched pid: none is an error.
        std::fs::create_dir_all(tree.0.join("2")).expect("mkdir");
        tree.file("3/stat", "garbage");
        tree.file("self/stat", &stat_line(1, "init", 0, 0, 0, 1));
        tree.file("4/stat", &stat_line(5, "liar", 0, 0, 0, 1));
        let mut sampler = Sampler::new(&tree.0);
        let (_, seen) = sampler.sample().expect("sample");
        assert_eq!(seen.processes, 1);
    }

    #[test]
    fn an_unreadable_total_keeps_the_baseline() {
        let tree = FakeTree::new("topcpu-total");
        fake_proc(&tree, 0);
        process(&tree, 7, "busy", 0, 0, 1);
        let mut sampler = Sampler::new(&tree.0);
        sampler.sample().expect("baseline");

        tree.file("stat", "intr 1 2 3\n");
        process(&tree, 7, "busy", 0, 100, 1);
        assert!(sampler.sample().is_err());

        // The next good sample measures the whole span since the baseline.
        fake_proc(&tree, 2_000);
        process(&tree, 7, "busy", 0, 200, 1);
        let (usage, _) = sampler.sample().expect("sample");
        assert_eq!(usage["busy"].percent, 10.0);

        // A total that went backwards (a CPU taken offline) measures nothing, then recovers.
        fake_proc(&tree, 1_000);
        assert!(sampler.sample().expect("sample").0.is_empty());
        let missing = Sampler::new(tree.0.join("absent")).sample();
        assert!(missing.is_err());
    }

    fn usage(name: &str, percent: f64) -> (String, Usage) {
        (
            name.to_string(),
            Usage {
                name: name.to_string(),
                percent,
                pid: 1,
                kernel_thread: false,
            },
        )
    }

    #[test]
    fn the_top_is_at_least_min_percent_busiest_first_at_most_count() {
        let by_name: BTreeMap<String, Usage> = [
            usage("a", 0.99),
            usage("b", 1.0),
            usage("c", 50.0),
            usage("d", 7.5),
            usage("e", 7.5),
            usage("f", 3.0),
        ]
        .into_iter()
        .collect();
        let names = |top: Vec<Usage>| top.into_iter().map(|u| u.name).collect::<Vec<_>>();
        assert_eq!(
            names(select_top(&by_name, 10, 1.0)),
            ["c", "d", "e", "f", "b"]
        );
        assert_eq!(names(select_top(&by_name, 2, 1.0)), ["c", "d"]);
        assert_eq!(names(select_top(&by_name, 0, 1.0)), Vec::<String>::new());
        assert_eq!(names(select_top(&by_name, 10, 0.0)).len(), 6);

        // Twelve busy names: only the ten busiest are posted.
        let many: BTreeMap<String, Usage> = (0..12)
            .map(|i| usage(&format!("p{i:02}"), 2.0 + i as f64))
            .collect();
        let top = names(select_top(&many, 10, 1.0));
        assert_eq!(top.len(), 10);
        assert_eq!(top[0], "p11");
        assert!(!top.contains(&"p00".to_string()) && !top.contains(&"p01".to_string()));
    }

    #[test]
    fn hidepid_is_read_from_the_proc_mount() {
        let stock = "22 1 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw\n";
        assert_eq!(hidepid_option(stock, "/proc"), None);
        let protected = "22 1 0:21 / /proc rw,nosuid shared:12 - proc proc rw\n\
                         812 22 0:77 / /proc rw,nosuid,nodev,noexec,relatime - proc proc \
                         rw,hidepid=invisible\n";
        assert_eq!(
            hidepid_option(protected, "/proc").as_deref(),
            Some("hidepid=invisible")
        );
        let numeric = "22 1 0:21 / /proc rw - proc proc rw,hidepid=2,gid=4\n";
        assert_eq!(
            hidepid_option(numeric, "/proc").as_deref(),
            Some("hidepid=2")
        );
        let off = "22 1 0:21 / /proc rw - proc proc rw,hidepid=0\n";
        assert_eq!(hidepid_option(off, "/proc"), None);
        // Another proc mount (a container's) is not ours.
        let other = "30 1 0:21 / /var/lib/x/proc rw - proc proc rw,hidepid=2\n";
        assert_eq!(hidepid_option(other, "/proc"), None);
    }
}
