//! `Written today on <name> disk`: the bytes written to a whole disk since local midnight.
//!
//! **Source.** The same `/proc/diskstats` "sectors written" counter the write-speed bar reads every
//! 5 s (512-byte sectors, whatever the device's sector size), per whole disk.
//!
//! **Accounting.** Every sample adds the disk's delta since its previous sample to the current
//! local day (the host's timezone). The day turns at local midnight: the first sample of a new day
//! starts it from 0. A delta that cannot be honest only sets a baseline: the first sample, a
//! counter that went backwards (a device reset, a re-attached disk), a clock that went backwards,
//! and a gap longer than three sample periods that crosses midnight (its bytes cannot be placed in
//! either day).
//!
//! **Posting.** Every 5 minutes, the day so far, in decimal GB (10⁹ bytes). A day with no measured
//! delta yet is not posted — never an invented 0; a day whose measurement began after midnight
//! (the probe was installed, or not running, at midnight) says from when in the comment.
//!
//! **Restarts.** The day's total and each disk's last counter live in
//! `$STATE_DIRECTORY/disk-written.json`, so a restart continues the day, and the writes made while
//! the probe was down count too. The counters restart at boot, so the file also records the boot
//! (`/proc/sys/kernel/random/boot_id`): after a reboot the old counters are not trusted and the
//! writes between the last sample before it and the first after it are not counted.
//!
//! **Which disk.** Records are keyed by the kernel name (`sda`), which is not stable: a reboot can
//! swap `sda` and `sdb`, a USB disk can take a name another disk held earlier. Each record
//! therefore carries the disk's identity ([`same_disk`]): its WWID or serial from sysfs, else the
//! mount points on it. A different disk under a known name starts the day afresh (from its first
//! sample, with the "measured since" comment); after a reboot the day is kept only when the
//! identity is known and matches. A disk not seen for more than a day is dropped from the file.

use std::collections::BTreeMap;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

use serde::{Deserialize, Serialize};

use crate::logging::Logger;

pub const FILE_NAME: &str = "disk-written.json";
/// Bytes per reported gigabyte: decimal, the unit disk endurance (TBW) is rated in.
pub const BYTES_PER_GB: f64 = 1_000_000_000.0;
const SECTOR_BYTES: u64 = 512;
const FILE_VERSION: u32 = 1;

/// `(local day number, second of that day)` for a Unix time in milliseconds, in the host's
/// timezone. Injectable so tests do not depend on the machine's zone.
pub type LocalTime = fn(i64) -> (i64, i64);

/// [`LocalTime`] of the host: `localtime_r`'s UTC offset (so DST is honored), UTC if unknown.
pub fn local_time(unix_ms: i64) -> (i64, i64) {
    let seconds = unix_ms.div_euclid(1000);
    let local = seconds + utc_offset(seconds).unwrap_or(0);
    (local.div_euclid(86_400), local.rem_euclid(86_400))
}

#[cfg(unix)]
fn utc_offset(seconds: i64) -> Option<i64> {
    // glibc's localtime_r reads the zone once per process; tzset re-reads TZ and /etc/localtime
    // (a stat when unchanged), so a `timedatectl set-timezone` applies without a restart.
    extern "C" {
        // POSIX <time.h>; not bound by the libc crate.
        fn tzset();
    }
    // SAFETY: tzset takes no arguments and only updates libc's own zone state.
    unsafe { tzset() };
    let time = libc::time_t::try_from(seconds).ok()?;
    // SAFETY: `tm` is plain data; localtime_r only writes into it.
    let mut tm: libc::tm = unsafe { std::mem::zeroed() };
    // SAFETY: both pointers are valid for the duration of the call.
    let result = unsafe { libc::localtime_r(&time, &mut tm) };
    if result.is_null() {
        return None;
    }
    // The field type differs between libc targets, hence `as`.
    #[allow(clippy::unnecessary_cast)]
    Some(tm.tm_gmtoff as i64)
}

#[cfg(not(unix))]
fn utc_offset(_seconds: i64) -> Option<i64> {
    None
}

/// A disk's counter at its last sample.
#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Baseline {
    pub sectors: u64,
    pub at_ms: i64,
    /// The local day of `at_ms`.
    pub day: i64,
}

/// One disk's day.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DayRecord {
    /// Local day number.
    pub day: i64,
    pub bytes: u64,
    /// At least one delta was accepted today; `false` ⇒ nothing to post.
    pub measured: bool,
    /// The local second of the day the measurement began, when that was not midnight.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub since_second: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub baseline: Option<Baseline>,
    /// Which disk the day belongs to (see [`same_disk`]).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub identity: Option<String>,
    /// Loaded from another boot: the day is kept only if the first sample's identity matches.
    #[serde(skip)]
    pub unverified: bool,
}

/// Whether two identities name the same disk. A WWID or serial must be equal; the mount-point
/// fallback (`mounts:<a>|<b>`) matches when the two sets share a mount point, so mounting another
/// partition of the same disk does not make it a different disk.
pub fn same_disk(a: &str, b: &str) -> bool {
    match (a.strip_prefix("mounts:"), b.strip_prefix("mounts:")) {
        (Some(a), Some(b)) => a
            .split('|')
            .any(|point| b.split('|').any(|other| other == point)),
        _ => a == b,
    }
}

/// Why a sample added nothing.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Skip {
    /// No trusted earlier counter (first sample, after a reboot, a disk seen again).
    Baseline,
    CounterReset,
    ClockBackwards,
    /// The probe missed samples across midnight.
    GapAcrossDays,
    /// Another physical disk now carries this name: its day starts afresh.
    OtherDisk,
}

/// The day of every metered disk, keyed by its `/proc/diskstats` name.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Ledger {
    pub disks: BTreeMap<String, DayRecord>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct LedgerFile {
    version: u32,
    /// The boot the baselines belong to.
    boot_id: String,
    disks: BTreeMap<String, DayRecord>,
}

impl Ledger {
    /// Record `disk`'s counter read at `now_ms`; returns the bytes added to its day, or why none.
    /// The new counter always becomes the baseline. `identity` names the physical disk now behind
    /// the name ([`same_disk`]).
    pub fn sample(
        &mut self,
        disk: &str,
        identity: Option<&str>,
        sectors: u64,
        now_ms: i64,
        local: LocalTime,
        period: Duration,
    ) -> Result<u64, Skip> {
        let (day, _) = local(now_ms);
        let record = self
            .disks
            .entry(disk.to_string())
            .or_insert_with(|| DayRecord {
                day,
                ..DayRecord::default()
            });
        let other_disk = match (record.identity.as_deref(), identity) {
            (Some(known), Some(now)) => !same_disk(known, now),
            // After a reboot an unknown identity cannot vouch for the day.
            _ => record.unverified,
        };
        record.unverified = false;
        if identity.is_some() {
            record.identity = identity.map(str::to_string);
        }
        if other_disk {
            // Another disk under this name: nothing of the old day or counter is its own.
            *record = DayRecord {
                day,
                identity: identity.map(str::to_string),
                baseline: Some(Baseline {
                    sectors,
                    at_ms: now_ms,
                    day,
                }),
                ..DayRecord::default()
            };
            return Err(Skip::OtherDisk);
        }
        // Checked before the day turns: a clock stepped back across midnight must not wipe the
        // day it came from (and re-post a small total for a day already posted in full).
        if let Some(previous) = record.baseline.as_ref() {
            if now_ms < previous.at_ms {
                record.baseline = Some(Baseline {
                    sectors,
                    at_ms: now_ms,
                    day,
                });
                return Err(Skip::ClockBackwards);
            }
        }
        if day > record.day {
            // Local midnight passed: a new day starts from 0. (A clock behind the day keeps
            // counting into it; nothing is posted until the clock reaches it again.)
            record.day = day;
            record.bytes = 0;
            record.measured = false;
            record.since_second = None;
        }
        let previous = record
            .baseline
            .replace(Baseline {
                sectors,
                at_ms: now_ms,
                day,
            })
            .ok_or(Skip::Baseline)?;
        if sectors < previous.sectors {
            return Err(Skip::CounterReset);
        }
        if now_ms < previous.at_ms {
            return Err(Skip::ClockBackwards);
        }
        let gap = u64::try_from(now_ms - previous.at_ms).unwrap_or(u64::MAX);
        let longest = u64::try_from(period.as_millis()).unwrap_or(u64::MAX) * 3;
        if gap > longest && previous.day != day {
            return Err(Skip::GapAcrossDays);
        }
        if !record.measured {
            record.measured = true;
            // A delta that began yesterday means the day was watched from midnight.
            record.since_second = (previous.day == day).then(|| local(previous.at_ms).1);
        }
        let bytes = (sectors - previous.sectors).saturating_mul(SECTOR_BYTES);
        record.bytes = record.bytes.saturating_add(bytes);
        Ok(bytes)
    }

    /// Drop the disks not seen since before yesterday (renamed away, unplugged for good), so the
    /// file does not grow with name churn.
    pub fn prune(&mut self, today: i64) {
        self.disks.retain(|_, record| record.day >= today - 1);
    }

    /// Forget the counters of every disk `keep` rejects (no longer in `/proc/diskstats`).
    pub fn forget_baselines_except(&mut self, keep: impl Fn(&str) -> bool) {
        for (disk, record) in self.disks.iter_mut() {
            if !keep(disk) {
                record.baseline = None;
            }
        }
    }

    /// A disk left `/proc/diskstats`: its next appearance starts a new baseline.
    pub fn forget_baseline(&mut self, disk: &str) {
        if let Some(record) = self.disks.get_mut(disk) {
            record.baseline = None;
        }
    }

    /// Today's value for `disk` — decimal GB, three decimals — and its comment; `None` when today
    /// has no measurement yet.
    pub fn today(
        &self,
        disk: &str,
        now_ms: i64,
        local: LocalTime,
    ) -> Option<(f64, Option<String>)> {
        let record = self.disks.get(disk)?;
        if !record.measured || record.day != local(now_ms).0 {
            return None;
        }
        let gigabytes = (record.bytes as f64 / BYTES_PER_GB * 1000.0).round() / 1000.0;
        // A minute of slack: a probe started at 00:00:30 watched the day.
        let comment = record
            .since_second
            .filter(|second| *second > 60)
            .map(|second| {
                format!(
                    "measured since {:02}:{:02} local time (writes before that are not counted)",
                    second / 3600,
                    second % 3600 / 60
                )
            });
        Some((gigabytes, comment))
    }

    /// Load the ledger; baselines of another boot are dropped (the counters restarted), the days
    /// are kept. A missing file is an empty ledger; an unusable one is logged and treated so.
    pub fn load(path: &Path, boot_id: &str, logger: &Logger) -> Ledger {
        let text = match fs::read_to_string(path) {
            Ok(text) => text,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ledger::default(),
            Err(error) => {
                logger.error(format!(
                    "disks: cannot read {}: {error}; today's written totals start afresh",
                    path.display()
                ));
                return Ledger::default();
            }
        };
        let file = match serde_json::from_str::<LedgerFile>(&text) {
            Ok(file) if file.version == FILE_VERSION => file,
            Ok(_) | Err(_) => {
                logger.error(format!(
                    "disks: {} is not a written-today ledger of this probe; today's written \
                     totals start afresh",
                    path.display()
                ));
                return Ledger::default();
            }
        };
        let mut disks = file.disks;
        if boot_id.is_empty() || file.boot_id != boot_id {
            // Another boot: the counters restarted and kernel names may have moved.
            if !disks.is_empty() {
                logger.info(format!(
                    "disks: the host rebooted (or its boot id is unreadable) since {} was saved; \
                     today's Written today totals continue, but the writes between the last \
                     sample before the reboot and the first after it are not counted",
                    path.display()
                ));
            }
            for record in disks.values_mut() {
                record.baseline = None;
                record.unverified = true;
            }
        }
        Ledger { disks }
    }

    /// Write atomically (temp file + fsync + rename).
    pub fn save(&self, path: &Path, boot_id: &str) -> io::Result<()> {
        let file = LedgerFile {
            version: FILE_VERSION,
            boot_id: boot_id.to_string(),
            disks: self.disks.clone(),
        };
        let temp = temp_path(path);
        {
            let mut out = fs::File::create(&temp)?;
            // Serializing plain owned data cannot fail.
            out.write_all(
                serde_json::to_string_pretty(&file)
                    .unwrap_or_default()
                    .as_bytes(),
            )?;
            out.write_all(b"\n")?;
            out.sync_all()?;
        }
        fs::rename(&temp, path).inspect_err(|_| {
            let _ = fs::remove_file(&temp);
        })
    }
}

fn temp_path(path: &Path) -> PathBuf {
    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(".tmp");
    path.with_file_name(name)
}

/// This boot's id (`/proc/sys/kernel/random/boot_id`); empty when unreadable, which makes every
/// persisted baseline untrusted.
pub fn read_boot_id(path: &Path) -> String {
    fs::read_to_string(path)
        .map(|text| text.trim().to_string())
        .unwrap_or_default()
}

#[cfg(test)]
pub mod tests {
    use super::*;
    use crate::logging::Level;
    use crate::probe_only::host::tests::FakeTree;

    const PERIOD: Duration = Duration::from_secs(5);
    /// 2026-09-29 00:00:00 UTC, ms (the tests' "local" time is UTC).
    pub const MIDNIGHT: i64 = 1_790_640_000_000;
    const MIN: i64 = 60_000;
    const HOUR: i64 = 60 * MIN;

    pub fn utc(unix_ms: i64) -> (i64, i64) {
        let seconds = unix_ms.div_euclid(1000);
        (seconds.div_euclid(86_400), seconds.rem_euclid(86_400))
    }

    #[test]
    fn a_day_accumulates_sectors_as_decimal_gigabytes() {
        let mut ledger = Ledger::default();
        let t = MIDNIGHT - 2_000;
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 1_000, t, utc, PERIOD),
            Err(Skip::Baseline)
        );
        // 2_000_000 sectors = 1.024 GB across midnight: counted in the new day, which was watched
        // from its start (no comment).
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 2_001_000, t + 5_000, utc, PERIOD),
            Ok(1_024_000_000)
        );
        assert_eq!(
            ledger.today("sdc", MIDNIGHT + 5 * MIN, utc),
            Some((1.024, None))
        );
        // Nothing written: a real 0 delta, the day stays.
        ledger
            .sample("sdc", Some("wwid:sdc"), 2_001_000, t + 10_000, utc, PERIOD)
            .unwrap();
        assert_eq!(
            ledger.today("sdc", MIDNIGHT + 6 * MIN, utc).unwrap().0,
            1.024
        );
    }

    #[test]
    fn midnight_starts_the_next_day_from_zero() {
        let mut ledger = Ledger::default();
        let evening = MIDNIGHT + 23 * HOUR + 59 * MIN + 50_000;
        ledger
            .sample("sdc", Some("wwid:sdc"), 0, evening - 5_000, utc, PERIOD)
            .ok();
        ledger
            .sample("sdc", Some("wwid:sdc"), 1_000_000, evening, utc, PERIOD)
            .unwrap();
        assert!(ledger.today("sdc", evening, utc).unwrap().0 > 0.5);
        // 00:00:05 the next day: the day before is over, the new one starts from what the first
        // sample across midnight measured (nothing here).
        let next = evening + 15_000;
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 1_000_000, next, utc, PERIOD),
            Ok(0)
        );
        assert_eq!(ledger.today("sdc", next, utc), Some((0.0, None)));
        // Before any sample of the next day, yesterday's total is not reported as today's.
        let mut stale = Ledger::default();
        stale
            .sample("sda", Some("wwid:sda"), 0, evening - 5_000, utc, PERIOD)
            .ok();
        stale
            .sample("sda", Some("wwid:sda"), 10, evening, utc, PERIOD)
            .unwrap();
        assert_eq!(stale.today("sda", next, utc), None);
    }

    #[test]
    fn a_reset_or_the_first_sample_is_skipped_never_zero() {
        let mut ledger = Ledger::default();
        let t = MIDNIGHT + 10 * HOUR;
        ledger
            .sample("sdc", Some("wwid:sdc"), 5_000, t, utc, PERIOD)
            .ok();
        assert_eq!(ledger.today("sdc", t, utc), None, "only a baseline yet");
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 100, t + 5_000, utc, PERIOD),
            Err(Skip::CounterReset)
        );
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 300, t + 10_000, utc, PERIOD),
            Ok(200 * 512)
        );
        // The measurement began at 10:00: the comment says so.
        let (value, comment) = ledger.today("sdc", t + 10_000, utc).unwrap();
        assert_eq!(value, 0.0);
        assert_eq!(
            comment.as_deref(),
            Some("measured since 10:00 local time (writes before that are not counted)")
        );
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 400, t, utc, PERIOD),
            Err(Skip::ClockBackwards)
        );
    }

    #[test]
    fn a_clock_stepped_back_across_midnight_does_not_wipe_the_day() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let after = MIDNIGHT + 24 * HOUR + 60_000; // 00:01 of the next day
        ledger.sample("sdc", d, 0, after - 5_000, utc, PERIOD).ok();
        ledger
            .sample("sdc", d, 2_000_000, after, utc, PERIOD)
            .unwrap();
        let today = ledger.disks["sdc"].day;
        // NTP steps the clock back to 23:58 the day before.
        let back = MIDNIGHT + 24 * HOUR - 2 * 60_000;
        assert_eq!(
            ledger.sample("sdc", d, 2_000_100, back, utc, PERIOD),
            Err(Skip::ClockBackwards)
        );
        assert_eq!(ledger.disks["sdc"].day, today, "the day is not turned back");
        assert_eq!(ledger.disks["sdc"].bytes, 1_024_000_000, "nor wiped");
        // Counting goes on into that day; nothing is posted while the clock is behind it.
        ledger
            .sample("sdc", d, 2_000_200, back + 5_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.today("sdc", back + 5_000, utc), None);
        assert_eq!(
            ledger.today("sdc", after + 5_000, utc).unwrap().0,
            1.024,
            "posted again once the clock is back in its day"
        );
    }

    #[test]
    fn a_gap_inside_the_day_counts_and_one_across_midnight_is_dropped() {
        let mut ledger = Ledger::default();
        let t = MIDNIGHT + 20 * HOUR;
        ledger
            .sample("sdc", Some("wwid:sdc"), 0, t, utc, PERIOD)
            .ok();
        // Two hours without a sample (the probe was stopped): all of it belongs to today.
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 1_000, t + 2 * HOUR, utc, PERIOD),
            Ok(1_000 * 512)
        );
        // Stopped from 23:00 to 01:00: cannot be split between the days.
        assert_eq!(
            ledger.sample("sdc", Some("wwid:sdc"), 2_000, t + 5 * HOUR, utc, PERIOD),
            Err(Skip::GapAcrossDays)
        );
        assert_eq!(ledger.today("sdc", t + 5 * HOUR, utc), None);
    }

    #[test]
    fn the_day_survives_a_restart_and_a_reboot_drops_only_the_counters() {
        let tree = FakeTree::new("disk-written");
        let path = tree.0.join(FILE_NAME);
        let logger = Logger::new(Level::Error, None);
        let t = MIDNIGHT + 9 * HOUR;
        let mut ledger = Ledger::default();
        ledger
            .sample("sdc", Some("wwid:sdc"), 0, t, utc, PERIOD)
            .ok();
        ledger
            .sample("sdc", Some("wwid:sdc"), 2_000_000, t + 5_000, utc, PERIOD)
            .unwrap();
        ledger.save(&path, "boot-a").unwrap();

        // Restarted in the same boot: the counter pairs with the persisted one — the writes made
        // while the probe was down count.
        let mut same = Ledger::load(&path, "boot-a", &logger);
        assert_eq!(same, ledger);
        assert_eq!(
            same.sample("sdc", Some("wwid:sdc"), 4_000_000, t + HOUR, utc, PERIOD),
            Ok(2_000_000 * 512)
        );
        assert_eq!(same.today("sdc", t + HOUR, utc).unwrap().0, 2.048);

        // After a reboot the kernel counter restarted: a baseline, and the day is kept.
        let mut rebooted = Ledger::load(&path, "boot-b", &logger);
        assert_eq!(
            rebooted.sample("sdc", Some("wwid:sdc"), 3_000_000, t + HOUR, utc, PERIOD),
            Err(Skip::Baseline)
        );
        assert_eq!(rebooted.today("sdc", t + HOUR, utc).unwrap().0, 1.024);
        // An unreadable boot id trusts no counter either.
        assert_eq!(Ledger::load(&path, "", &logger).disks["sdc"].baseline, None);
        // Missing and foreign files are a fresh start.
        assert_eq!(
            Ledger::load(&tree.0.join("absent.json"), "boot-a", &logger),
            Ledger::default()
        );
        tree.file(FILE_NAME, r#"{"version": 9, "bootId": "x", "disks": {}}"#);
        assert_eq!(Ledger::load(&path, "boot-a", &logger), Ledger::default());
    }

    #[test]
    fn a_reboot_that_swaps_disk_names_does_not_carry_one_disks_day_to_another() {
        let tree = FakeTree::new("disk-written-swap");
        let path = tree.0.join(FILE_NAME);
        let logger = Logger::new(Level::Error, None);
        let t = MIDNIGHT + 9 * HOUR;
        let (a, b) = (
            Some("wwid:naa.5000c500a1b2c3d4"),
            Some("wwid:naa.5000c500e5f6a7b8"),
        );
        let mut ledger = Ledger::default();
        for (name, identity, written) in [("sda", a, 2_000_000), ("sdb", b, 4_000_000)] {
            ledger.sample(name, identity, 0, t, utc, PERIOD).ok();
            ledger
                .sample(name, identity, written, t + 5_000, utc, PERIOD)
                .unwrap();
        }
        ledger.save(&path, "boot-a").unwrap();

        // Rebooted at 14:00; the kernel now calls disk B `sda` and disk A `sdb`.
        let later = t + 5 * HOUR;
        let mut swapped = Ledger::load(&path, "boot-b", &logger);
        assert_eq!(
            swapped.sample("sda", b, 10, later, utc, PERIOD),
            Err(Skip::OtherDisk)
        );
        assert_eq!(
            swapped.sample("sdb", a, 10, later, utc, PERIOD),
            Err(Skip::OtherDisk)
        );
        assert_eq!(
            swapped.today("sda", later, utc),
            None,
            "nothing measured yet"
        );
        swapped
            .sample("sda", b, 2_010, later + 5_000, utc, PERIOD)
            .unwrap();
        let (value, comment) = swapped.today("sda", later + 5_000, utc).unwrap();
        assert_eq!(value, 0.001);
        assert_eq!(
            comment.as_deref(),
            Some("measured since 14:00 local time (writes before that are not counted)")
        );

        // The same reboot without a swap keeps each day.
        let mut kept = Ledger::load(&path, "boot-b", &logger);
        assert_eq!(
            kept.sample("sda", a, 10, later, utc, PERIOD),
            Err(Skip::Baseline)
        );
        assert_eq!(kept.today("sda", later, utc).unwrap().0, 1.024);
        // …but an identity that cannot be read after a reboot cannot vouch for the day.
        let mut unknown = Ledger::load(&path, "boot-b", &logger);
        assert_eq!(
            unknown.sample("sdb", None, 10, later, utc, PERIOD),
            Err(Skip::OtherDisk)
        );

        // Same boot, another disk plugged in under a name held earlier today.
        let mut same_boot = Ledger::load(&path, "boot-a", &logger);
        assert_eq!(
            same_boot.sample("sdb", Some("wwid:usb-stick"), 5, later, utc, PERIOD),
            Err(Skip::OtherDisk)
        );

        // A disk not seen since before yesterday is dropped from the file.
        let mut old = Ledger::load(&path, "boot-a", &logger);
        old.prune(utc(t).0 + 1);
        assert_eq!(old.disks.len(), 2, "seen yesterday: kept");
        old.prune(utc(t).0 + 2);
        assert!(old.disks.is_empty());
    }

    #[test]
    fn the_mount_point_fallback_matches_on_any_shared_mount_point() {
        assert!(same_disk(
            "mounts:/mnt/mediacentr|/mnt/oldlinux",
            "mounts:/mnt/oldlinux"
        ));
        assert!(!same_disk("mounts:/mnt/wd4tb", "mounts:/mnt/mediacentr"));
        assert!(same_disk("wwid:x", "wwid:x"));
        assert!(!same_disk("wwid:x", "mounts:/"));
    }

    #[cfg(unix)]
    #[test]
    fn local_time_follows_the_utc_offset() {
        // Whatever the zone of the machine running the tests, the offset is whole minutes and
        // within ±14 h.
        let (day, second) = local_time(MIDNIGHT + 12 * HOUR);
        let local = day * 86_400 + second;
        let offset = local - (MIDNIGHT + 12 * HOUR) / 1000;
        assert!(offset.abs() <= 14 * 3600 && offset % 60 == 0, "{offset}");
    }
}
