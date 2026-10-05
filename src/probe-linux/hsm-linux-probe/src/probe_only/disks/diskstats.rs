//! Disk write speed: the whole disk under a filesystem, and its write rate from `/proc/diskstats`.
//!
//! Both reads are kernel memory (`/sys/dev/block/…/uevent`, `/proc/diskstats`): they never issue a
//! command to the disk, so they cannot wake a sleeping one.
//!
//! **Which disk.** The mount's `major:minor` names the block device in `/sys/dev/block/`; a
//! partition (`DEVTYPE=partition`) is replaced by its parent disk (`sda2` → `sda`), whose counters
//! cover every partition on it. A mount whose device number is not a block device (a
//! FUSE-anonymous device) falls back to the source's name in `/sys/class/block/`. Nothing found ⇒
//! no write-speed sensor for that filesystem.
//!
//! **The rate** is Δ sectors written × 512 bytes over the measured interval, in MB/s (1 MB =
//! 1024², the Windows `Disk Write Bytes/sec` ÷ 1024² the managed sensor uses). The first sample
//! only sets the baseline, and so does a counter that went backwards (a device reset) or an
//! interval that is not plausible for the sampling period — those samples are skipped, never
//! posted as 0.

use std::collections::BTreeMap;
use std::path::Path;
use std::time::{Duration, Instant};

/// `/proc/diskstats` counters are always in 512-byte sectors, whatever the device's sector size.
const SECTOR_BYTES: f64 = 512.0;
const BYTES_PER_MB: f64 = 1024.0 * 1024.0;

/// One device's sector counters, both from its one `/proc/diskstats` line.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct Sectors {
    pub written: u64,
    pub read: u64,
}

/// Sectors written and read per device name.
pub fn parse_diskstats(text: &str) -> BTreeMap<String, Sectors> {
    text.lines()
        .filter_map(|line| {
            let fields: Vec<&str> = line.split_whitespace().collect();
            // major minor name reads merged sectors_read ms writes merged sectors_written ...
            let name = fields.get(2)?;
            let read = fields.get(5)?.parse().ok()?;
            let written = fields.get(9)?.parse().ok()?;
            Some((name.to_string(), Sectors { written, read }))
        })
        .collect()
}

/// `KEY=value` lines of a sysfs `uevent` file.
fn uevent(path: &Path) -> Option<BTreeMap<String, String>> {
    let text = std::fs::read_to_string(path).ok()?;
    Some(
        text.lines()
            .filter_map(|line| line.split_once('='))
            .map(|(key, value)| (key.to_string(), value.to_string()))
            .collect(),
    )
}

/// The whole disk (a `/proc/diskstats` name) under a filesystem mounted from `source` with
/// device number `device` (`major:minor`), read from the sysfs at `sys_root`.
pub fn whole_disk(sys_root: &Path, device: &str, source: &str) -> Option<String> {
    let by_number = sys_root.join("dev/block").join(device);
    let entry = if by_number.join("uevent").exists() {
        by_number
    } else {
        let name = source.strip_prefix("/dev/")?.rsplit('/').next()?;
        sys_root.join("class/block").join(name)
    };
    let own = uevent(&entry.join("uevent"))?;
    if own.get("DEVTYPE").map(String::as_str) == Some("partition") {
        // `<entry>/..` is resolved after the kernel follows the entry's symlink: the parent disk.
        let parent = uevent(&entry.join("..").join("uevent"))?;
        parent.get("DEVNAME").cloned()
    } else {
        own.get("DEVNAME").cloned()
    }
}

/// Write-rate state of one disk (and its read rate: the same arithmetic on "sectors read").
#[derive(Debug)]
pub struct WriteRate {
    period: Duration,
    last: Option<(u64, Instant)>,
}

/// Why a sample produced no rate.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Skip {
    /// The first sample only records the baseline.
    Baseline,
    /// The counter went backwards (a device reset or a re-attached disk).
    CounterReset,
    /// The interval is implausible for the sampling period (the host was suspended, the thread
    /// was held up): a rate over it would be an average, not a sample.
    AbnormalInterval,
}

impl WriteRate {
    pub fn new(period: Duration) -> Self {
        Self { period, last: None }
    }

    /// MB/s since the previous sample, or why there is none. Every call becomes the new baseline.
    pub fn sample(&mut self, sectors_written: u64, now: Instant) -> Result<f64, Skip> {
        let previous = self.last.replace((sectors_written, now));
        let (last_sectors, last_at) = previous.ok_or(Skip::Baseline)?;
        if sectors_written < last_sectors {
            return Err(Skip::CounterReset);
        }
        let elapsed = now.saturating_duration_since(last_at);
        if elapsed < self.period / 2 || elapsed > self.period * 3 {
            return Err(Skip::AbnormalInterval);
        }
        let bytes = (sectors_written - last_sectors) as f64 * SECTOR_BYTES;
        Ok(bytes / elapsed.as_secs_f64() / BYTES_PER_MB)
    }
}

#[cfg(test)]
pub mod tests {
    use super::*;
    use crate::probe_only::host::tests::FakeTree;

    /// `/proc/diskstats` of garage-server (2026-09-29).
    pub const GARAGE_DISKSTATS: &str = include_str!("fixtures/garage-diskstats.txt");

    #[test]
    fn diskstats_parses_sectors_written_and_read_per_device() {
        let stats = parse_diskstats(GARAGE_DISKSTATS);
        assert_eq!(stats["sda"].written, 307_480_584);
        assert_eq!(stats["sdb"].written, 39_386_608);
        assert_eq!(stats["sdb5"].written, 280);
        assert_eq!(stats["sdc"].written, 683_671_624);
        // "sectors read" is the third counter of the same line.
        assert_eq!(stats["sda"].read, 530_467_632);
        assert_eq!(stats["sdb"].read, 747_765_872);
        assert_eq!(stats["sdc"].read, 161_806_974);
        assert_eq!(stats.len(), 12);
        assert!(parse_diskstats("short line\n").is_empty());
    }

    /// garage-server's sysfs for its four mounted partitions: `/sys/dev/block/<maj:min>` links into
    /// the device tree, each partition directory sits inside its disk's (as in the kernel).
    #[cfg(unix)]
    pub fn garage_sysfs(tree: &FakeTree) {
        for (disk, disk_number, partitions) in [
            ("sda", "8:0", &[("sda2", "8:2")][..]),
            ("sdb", "8:16", &[("sdb1", "8:17"), ("sdb5", "8:21")][..]),
            ("sdc", "8:32", &[("sdc1", "8:33")][..]),
        ] {
            tree.file(
                &format!("sys/devices/block/{disk}/uevent"),
                &format!("MAJOR=8\nDEVNAME={disk}\nDEVTYPE=disk\n"),
            );
            tree.link(
                &format!("sys/dev/block/{disk_number}"),
                &format!("../../devices/block/{disk}"),
            );
            for (partition, number) in partitions {
                tree.file(
                    &format!("sys/devices/block/{disk}/{partition}/uevent"),
                    &format!(
                        "MAJOR=8\nDEVNAME={partition}\nDEVTYPE=partition\nPARTNAME=Basic data partition\n"
                    ),
                );
                tree.link(
                    &format!("sys/dev/block/{number}"),
                    &format!("../../devices/block/{disk}/{partition}"),
                );
                tree.link(
                    &format!("sys/class/block/{partition}"),
                    &format!("../../devices/block/{disk}/{partition}"),
                );
            }
        }
    }

    /// Symlinks need a Unix host; elsewhere (the probe is Linux-only) the fake sysfs stays empty
    /// and the write-speed sensors simply do not register.
    #[cfg(not(unix))]
    pub fn garage_sysfs(_tree: &FakeTree) {}

    #[cfg(unix)]
    #[test]
    fn partitions_map_to_their_whole_disk() {
        let tree = FakeTree::new("sysfs");
        garage_sysfs(&tree);
        let sys = tree.0.join("sys");
        assert_eq!(whole_disk(&sys, "8:2", "/dev/sda2").as_deref(), Some("sda"));
        assert_eq!(
            whole_disk(&sys, "8:17", "/dev/sdb1").as_deref(),
            Some("sdb")
        );
        assert_eq!(
            whole_disk(&sys, "8:21", "/dev/sdb5").as_deref(),
            Some("sdb")
        );
        assert_eq!(
            whole_disk(&sys, "8:33", "/dev/sdc1").as_deref(),
            Some("sdc")
        );
        // A whole-disk filesystem is its own disk.
        assert_eq!(whole_disk(&sys, "8:16", "/dev/sdb").as_deref(), Some("sdb"));
        // A FUSE-anonymous device number falls back to the source's name.
        assert_eq!(
            whole_disk(&sys, "0:57", "/dev/sda2").as_deref(),
            Some("sda")
        );
        // Neither known: no disk, so no write-speed sensor.
        assert_eq!(whole_disk(&sys, "0:57", "pool"), None);
        assert_eq!(whole_disk(&sys, "0:57", "/dev/mapper/nothing"), None);
    }

    #[test]
    fn write_rate_is_megabytes_per_second_over_the_measured_interval() {
        let period = Duration::from_secs(5);
        let mut rate = WriteRate::new(period);
        let t0 = Instant::now();
        assert_eq!(rate.sample(1_000, t0), Err(Skip::Baseline));
        // 20 480 sectors = 10 MiB in 5 s = 2 MB/s.
        let mb_s = rate.sample(1_000 + 20_480, t0 + period).expect("rate");
        assert!((mb_s - 2.0).abs() < 1e-9, "{mb_s}");
        // Nothing written: a real 0, posted.
        assert_eq!(rate.sample(21_480, t0 + period * 2), Ok(0.0));
    }

    #[test]
    fn resets_and_implausible_intervals_are_skipped_not_zero() {
        let period = Duration::from_secs(5);
        let mut rate = WriteRate::new(period);
        let t0 = Instant::now();
        rate.sample(10_000, t0).unwrap_err();
        assert_eq!(rate.sample(5_000, t0 + period), Err(Skip::CounterReset));
        // The reset sample is the new baseline.
        assert!(rate.sample(5_512, t0 + period * 2).is_ok());
        // A stall of a minute (suspend, a held-up thread) is not a 5 s sample.
        assert_eq!(
            rate.sample(9_000, t0 + period * 2 + Duration::from_secs(60)),
            Err(Skip::AbnormalInterval)
        );
        // Two ticks fired back to back.
        let t1 = t0 + period * 2 + Duration::from_secs(60);
        assert_eq!(
            rate.sample(9_100, t1 + Duration::from_millis(100)),
            Err(Skip::AbnormalInterval)
        );
        assert!(rate
            .sample(9_200, t1 + Duration::from_millis(100) + period)
            .is_ok());
    }
}
