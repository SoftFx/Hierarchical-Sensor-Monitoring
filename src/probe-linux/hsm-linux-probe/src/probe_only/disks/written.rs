//! `Written per day on <name> disk`: the bytes written to a whole disk during one local day,
//! posted once, just before local midnight (#1498; `Written today`, posted every 5 minutes, until
//! 0.6.1). `Read per day on <name> disk` (#1506) is its mirror for the bytes read: the same
//! samples, the same record and every rule below, with its own total ([`Tally`]).
//!
//! **Source.** The same `/proc/diskstats` "sectors written" counter the write-speed bar reads every
//! 5 s (512-byte sectors, whatever the device's sector size), per whole disk; "sectors read" comes
//! from the same line of the same read.
//!
//! **Accounting.** Every sample adds the disk's delta since its previous sample to the current
//! local day (the host's timezone). The day turns at local midnight: the first sample of a new day
//! starts it from 0. A delta that cannot be honest only sets a baseline: the first sample, a
//! counter that went backwards (a device reset, a re-attached disk), a clock that went backwards,
//! and a gap longer than three sample periods that crosses midnight (its bytes cannot be placed in
//! either day).
//!
//! **Posting.** Once per day: the day's total, in decimal GB (10⁹ bytes), in the day's last
//! seconds (the caller's final-reading window). What is written after the post, before midnight,
//! counts towards the next day. A day with no measured delta is not posted — never an invented 0;
//! a day whose measurement began after midnight (the probe was installed, or not running, at
//! midnight) says from when in the comment. The ledger remembers the last day posted, so a restart
//! inside the window does not post it twice. A day whose window was missed is never posted and is
//! logged once: the probe being down at the first sample after start
//! ([`Ledger::unposted_days`]), a window missed while running when the day rolls over
//! ([`Ledger::missed`]).
//!
//! **Restarts.** The day's total and each disk's last counter live in
//! `$STATE_DIRECTORY/disk-written.json`, so a restart continues the day, and the writes made while
//! the probe was down count too. The counters restart at boot, so the file also records the boot
//! (`/proc/sys/kernel/random/boot_id`): after a reboot the old counters are not trusted and the
//! writes between the last sample before it and the first after it are not counted. The read
//! fields are additive (`serde(default)`): a file saved before them (0.7.0) loads, its written day
//! continues and the read day starts at the first sample (a baseline); an older probe ignores them.
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

use super::diskstats::Sectors;
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

/// A disk's counters at its last sample.
#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Baseline {
    /// Sectors written.
    pub sectors: u64,
    pub at_ms: i64,
    /// The local day of `at_ms`.
    pub day: i64,
    /// Sectors read; `None` in a ledger saved before #1506, so the read day starts at the next
    /// sample.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub read_sectors: Option<u64>,
}

impl Baseline {
    fn at(sectors: Sectors, at_ms: i64, day: i64) -> Self {
        Self {
            sectors: sectors.written,
            at_ms,
            day,
            read_sectors: Some(sectors.read),
        }
    }
}

/// Which of a disk's two per-day totals.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub enum Kind {
    Written,
    Read,
}

impl Kind {
    /// The sensor the total is posted to, for log lines.
    pub fn sensor(self) -> &'static str {
        match self {
            Kind::Written => "Written per day",
            Kind::Read => "Read per day",
        }
    }

    /// `1.024 GB written`.
    pub fn amount(self, gigabytes: f64) -> String {
        match self {
            Kind::Written => format!("{gigabytes} GB written"),
            Kind::Read => format!("{gigabytes} GB read"),
        }
    }

    fn what(self) -> &'static str {
        match self {
            Kind::Written => "writes",
            Kind::Read => "reads",
        }
    }
}

/// One counter's share of a disk's day.
#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Tally {
    pub bytes: u64,
    /// At least one delta was accepted today; `false` ⇒ nothing to post.
    pub measured: bool,
    /// The local second of the day the measurement began, when that was not midnight.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub since_second: Option<i64>,
}

impl Tally {
    /// Add a delta of `sectors`; the first of the day records since when (`since`).
    fn add(&mut self, sectors: u64, since: impl FnOnce() -> Option<i64>) -> u64 {
        if !self.measured {
            self.measured = true;
            self.since_second = since();
        }
        let bytes = sectors.saturating_mul(SECTOR_BYTES);
        self.bytes = self.bytes.saturating_add(bytes);
        bytes
    }

    /// The total in decimal GB, three decimals; `None` when nothing was measured.
    fn gigabytes(&self) -> Option<f64> {
        self.measured
            .then(|| (self.bytes as f64 / BYTES_PER_GB * 1000.0).round() / 1000.0)
    }
}

/// One disk's day: the written total (inline, as before #1506) and the read one.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DayRecord {
    /// Local day number.
    pub day: i64,
    /// `bytes`, `measured` and `sinceSecond` at the top level, as a 0.7.0 ledger has them.
    #[serde(flatten)]
    pub written: Tally,
    /// Absent from a ledger saved before #1506: nothing read measured yet.
    #[serde(default)]
    pub read: Tally,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub baseline: Option<Baseline>,
    /// Which disk the day belongs to (see [`same_disk`]).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub identity: Option<String>,
    /// Loaded from another boot: the day is kept only if the first sample's identity matches.
    #[serde(skip)]
    pub unverified: bool,
}

impl DayRecord {
    pub fn tally(&self, kind: Kind) -> &Tally {
        match kind {
            Kind::Written => &self.written,
            Kind::Read => &self.read,
        }
    }

    /// The day as lost: each measured total in GB (`None` when that counter measured nothing).
    fn unposted(&self, disk: &str) -> Unposted {
        Unposted {
            disk: disk.to_string(),
            day: self.day,
            written: self.written.gigabytes(),
            read: self.read.gigabytes(),
        }
    }
}

/// A measured day that is never posted (its post window was missed), for one log line naming
/// both totals.
#[derive(Clone, Debug, PartialEq)]
pub struct Unposted {
    pub disk: String,
    pub day: i64,
    /// GB written, three decimals; `None` when nothing was measured.
    pub written: Option<f64>,
    /// GB read, likewise.
    pub read: Option<f64>,
}

impl Unposted {
    /// See [`describe_totals`].
    pub fn describe(&self) -> (String, String) {
        describe_totals([(Kind::Written, self.written), (Kind::Read, self.read)])
    }
}

/// The sensors and the amounts of a disk's measured totals, for one log line naming both kinds:
/// `("Written per day and Read per day", "1.024 GB written and 0.5 GB read")`. A total that is
/// `None` (nothing measured) is left out.
pub fn describe_totals(totals: impl IntoIterator<Item = (Kind, Option<f64>)>) -> (String, String) {
    let measured: Vec<(Kind, f64)> = totals
        .into_iter()
        .filter_map(|(kind, gigabytes)| gigabytes.map(|gigabytes| (kind, gigabytes)))
        .collect();
    let sensors: Vec<&str> = measured.iter().map(|(kind, _)| kind.sensor()).collect();
    let amounts: Vec<String> = measured
        .iter()
        .map(|(kind, gigabytes)| kind.amount(*gigabytes))
        .collect();
    (sensors.join(" and "), amounts.join(" and "))
}

/// Whether two identities name the same disk; `None` when they cannot tell. A WWID or serial must
/// be equal; the mount-point fallback (`mounts:<a>|<b>`) matches when the two sets share a mount
/// point, so mounting another partition of the same disk does not make it a different disk. A
/// hardware identity and a fallback one are of different kinds (a WWID read that failed in one
/// run): they cannot tell.
pub fn same_disk(a: &str, b: &str) -> Option<bool> {
    match (a.strip_prefix("mounts:"), b.strip_prefix("mounts:")) {
        (Some(a), Some(b)) => Some(
            a.split('|')
                .any(|point| b.split('|').any(|other| other == point)),
        ),
        (None, None) => Some(a == b),
        _ => None,
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
    /// The last local day whose final reading was posted.
    pub posted_day: Option<i64>,
    /// Days that ended measured but unposted while the probe ran (the post window was missed —
    /// a suspend, unreadable `/proc/diskstats`), drained by the caller's log.
    pub missed: Vec<Unposted>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct LedgerFile {
    version: u32,
    /// The boot the baselines belong to.
    boot_id: String,
    disks: BTreeMap<String, DayRecord>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    posted_day: Option<i64>,
}

impl Ledger {
    /// The write tests' shorthand (they predate the read total): a written counter, nothing read.
    #[cfg(test)]
    pub fn sample(
        &mut self,
        disk: &str,
        identity: Option<&str>,
        sectors: u64,
        now_ms: i64,
        local: LocalTime,
        period: Duration,
    ) -> Result<u64, Skip> {
        let sectors = Sectors {
            written: sectors,
            read: 0,
        };
        self.sample_counters(disk, identity, sectors, now_ms, local, period)
    }

    /// Record `disk`'s counters read at `now_ms`; returns the bytes written added to its day, or
    /// why none. The new counters always become the baseline. `identity` names the physical disk
    /// now behind the name ([`same_disk`]). The read total follows every rule of the written one,
    /// from the same sample; on its own it only skips a read counter that went backwards and the
    /// first sample after a ledger without one (both only set its baseline). A counter reset is
    /// per counter: the written counter alone going backwards (`CounterReset`) still counts the
    /// sample's read delta, as a read reset leaves the written delta counted.
    pub fn sample_counters(
        &mut self,
        disk: &str,
        identity: Option<&str>,
        sectors: Sectors,
        now_ms: i64,
        local: LocalTime,
        period: Duration,
    ) -> Result<u64, Skip> {
        let (day, _) = local(now_ms);
        // Once today is posted, what is still written before midnight counts towards tomorrow:
        // counted once, never lost between the post and midnight.
        let posted_day = self.posted_day;
        let target = match posted_day {
            Some(posted) if posted >= day => day + 1,
            _ => day,
        };
        let missed = &mut self.missed;
        let record = self
            .disks
            .entry(disk.to_string())
            .or_insert_with(|| DayRecord {
                day: target,
                ..DayRecord::default()
            });
        let known = record.identity.clone();
        let verdict = match (known.as_deref(), identity) {
            (Some(known), Some(now)) => same_disk(known, now),
            _ => None,
        };
        // An identity that cannot tell keeps the day within a boot; after a reboot it cannot
        // vouch for it.
        let other_disk = verdict.map_or(record.unverified, |same| !same);
        record.unverified = false;
        // Keep a hardware identity rather than trade it for the mount-point fallback.
        let keeps_hardware = verdict.is_none()
            && known
                .as_deref()
                .is_some_and(|id| !id.starts_with("mounts:"));
        if identity.is_some() && !keeps_hardware {
            record.identity = identity.map(str::to_string);
        }
        if other_disk {
            // Another disk under this name: nothing of the old day or counter is its own.
            *record = DayRecord {
                day,
                identity: identity.map(str::to_string),
                baseline: Some(Baseline::at(sectors, now_ms, day)),
                ..DayRecord::default()
            };
            return Err(Skip::OtherDisk);
        }
        // Checked before the day turns: a clock stepped back across midnight must not wipe the
        // day it came from (and re-post a small total for a day already posted in full).
        if let Some(previous) = record.baseline.as_ref() {
            if now_ms < previous.at_ms {
                record.baseline = Some(Baseline::at(sectors, now_ms, day));
                return Err(Skip::ClockBackwards);
            }
        }
        if target > record.day {
            // Local midnight passed (or today was posted): a new day starts from 0. A day that
            // ends here measured but unposted missed its post window while the probe ran — it is
            // reported, never posted late. (A clock behind the day keeps counting into it.)
            if (record.written.measured || record.read.measured)
                && posted_day.is_none_or(|posted| posted < record.day)
            {
                missed.push(record.unposted(disk));
            }
            record.day = target;
            record.written = Tally::default();
            record.read = Tally::default();
        }
        let previous = record
            .baseline
            .replace(Baseline::at(sectors, now_ms, day))
            .ok_or(Skip::Baseline)?;
        let clock_back = now_ms < previous.at_ms;
        let gap = u64::try_from(now_ms - previous.at_ms).unwrap_or(u64::MAX);
        let longest = u64::try_from(period.as_millis()).unwrap_or(u64::MAX) * 3;
        let gap_across_days = gap > longest && previous.day != day;
        // A delta that began yesterday — or one counted towards tomorrow after today's post —
        // means the day was watched from its start.
        let since = || (previous.day == day && target == day).then(|| local(previous.at_ms).1);
        // Each counter's own reset only drops its own delta: the read one still counts when the
        // written counter alone went backwards (and the reverse, below).
        let read_delta = previous
            .read_sectors
            .filter(|before| sectors.read >= *before)
            .map(|before| sectors.read - before);
        if sectors.written < previous.sectors {
            if let Some(delta) = read_delta.filter(|_| !clock_back && !gap_across_days) {
                record.read.add(delta, since);
            }
            return Err(Skip::CounterReset);
        }
        if clock_back {
            return Err(Skip::ClockBackwards);
        }
        if gap_across_days {
            return Err(Skip::GapAcrossDays);
        }
        let bytes = record
            .written
            .add(sectors.written - previous.sectors, since);
        if let Some(delta) = read_delta {
            record.read.add(delta, since);
        }
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

    /// The write tests' shorthand for [`Ledger::day_total`] of the written total.
    #[cfg(test)]
    pub fn today(
        &self,
        disk: &str,
        now_ms: i64,
        local: LocalTime,
    ) -> Option<(f64, Option<String>)> {
        self.day_total(disk, Kind::Written, now_ms, local)
    }

    /// Today's `kind` total for `disk` — decimal GB, three decimals — and its comment; `None`
    /// when today has no measurement of it yet.
    pub fn day_total(
        &self,
        disk: &str,
        kind: Kind,
        now_ms: i64,
        local: LocalTime,
    ) -> Option<(f64, Option<String>)> {
        let record = self.disks.get(disk)?;
        // A day behind the clock is over: never report it as today. A clock behind the day (a DST
        // fall-back at local midnight, a step back) keeps counting into that day and keeps posting
        // it, so the sensor does not go silent into its TTL.
        if record.day < local(now_ms).0 {
            return None;
        }
        let tally = record.tally(kind);
        let gigabytes = tally.gigabytes()?;
        // A minute of slack: a probe started at 00:00:30 watched the day.
        let comment = tally
            .since_second
            .filter(|second| *second > 60)
            .map(|second| {
                format!(
                    "measured since {:02}:{:02} local time ({} before that are not counted)",
                    second / 3600,
                    second % 3600 / 60,
                    kind.what()
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
                    "disks: cannot read {}: {error}; today's written and read totals start afresh",
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
                     and read totals start afresh",
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
                     today's written and read totals continue, but what was written and read \
                     between the last sample before the reboot and the first after it is not \
                     counted",
                    path.display()
                ));
            }
            for record in disks.values_mut() {
                record.baseline = None;
                record.unverified = true;
            }
        }
        Ledger {
            disks,
            posted_day: file.posted_day,
            missed: Vec::new(),
        }
    }

    /// Days that ended with a measured total but no final post: the probe was not running at
    /// their midnight. For the start-up log; those days are never posted. Each
    /// is reported here only: its record is marked unmeasured, so the day turn does not report it
    /// again as [`Ledger::missed`] (#1489).
    pub fn unposted_days(&mut self, today: i64) -> Vec<Unposted> {
        let posted_day = self.posted_day;
        self.disks
            .iter_mut()
            .filter(|(_, record)| {
                (record.written.measured || record.read.measured)
                    && record.day < today
                    && posted_day.is_none_or(|posted| posted < record.day)
            })
            .map(|(disk, record)| {
                let lost = record.unposted(disk);
                record.written.measured = false;
                record.read.measured = false;
                lost
            })
            .collect()
    }

    /// Write atomically (temp file + fsync + rename).
    pub fn save(&self, path: &Path, boot_id: &str) -> io::Result<()> {
        let file = LedgerFile {
            version: FILE_VERSION,
            boot_id: boot_id.to_string(),
            disks: self.disks.clone(),
            posted_day: self.posted_day,
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

/// `YYYY-MM-DD` of a local day number (days since 1970-01-01), for log lines.
pub fn day_label(day: i64) -> String {
    // Civil-from-days (Howard Hinnant's algorithm), proleptic Gregorian.
    let z = day + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = yoe + era * 400 + i64::from(m <= 2);
    format!("{y:04}-{m:02}-{d:02}")
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

    fn lost(disk: &str, day: i64, written: f64, read: f64) -> Unposted {
        Unposted {
            disk: disk.to_string(),
            day,
            written: Some(written),
            read: Some(read),
        }
    }

    /// One sample's two counters.
    fn both(written: u64, read: u64) -> Sectors {
        Sectors { written, read }
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
        assert_eq!(
            ledger.disks["sdc"].written.bytes, 1_024_000_000,
            "nor wiped"
        );
        // Counting goes on into that day, and it keeps being posted (a DST fall-back at local
        // midnight must not leave the sensor silent into its TTL).
        ledger
            .sample("sdc", d, 2_000_200, back + 5_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.today("sdc", back + 5_000, utc).unwrap().0, 1.024);
        // A day behind the clock is never reported as today.
        assert_eq!(ledger.today("sdc", after + 24 * HOUR, utc), None);
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
        assert_eq!(
            same_disk(
                "mounts:/mnt/mediacentr|/mnt/oldlinux",
                "mounts:/mnt/oldlinux"
            ),
            Some(true)
        );
        assert_eq!(
            same_disk("mounts:/mnt/wd4tb", "mounts:/mnt/mediacentr"),
            Some(false)
        );
        assert_eq!(same_disk("wwid:x", "wwid:x"), Some(true));
        assert_eq!(same_disk("wwid:x", "serial:x"), Some(false));
        // A WWID that could not be read in one run: the kinds differ, it cannot tell.
        assert_eq!(same_disk("wwid:x", "mounts:/"), None);
    }

    #[test]
    fn a_failed_wwid_read_does_not_make_the_same_disk_another() {
        let mut ledger = Ledger::default();
        let t = MIDNIGHT + 9 * HOUR;
        ledger.sample("sdc", Some("wwid:x"), 0, t, utc, PERIOD).ok();
        ledger
            .sample("sdc", Some("wwid:x"), 2_000_000, t + 5_000, utc, PERIOD)
            .unwrap();
        // Same boot, this run could only read the mount points: the day stays, and the hardware
        // identity is kept.
        assert!(ledger
            .sample("sdc", Some("mounts:/"), 2_000_100, t + 10_000, utc, PERIOD)
            .is_ok());
        assert_eq!(ledger.disks["sdc"].identity.as_deref(), Some("wwid:x"));
        assert_eq!(ledger.today("sdc", t + 10_000, utc).unwrap().0, 1.024);
    }

    #[test]
    fn writes_after_the_days_post_count_towards_the_next_day() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let evening = MIDNIGHT + 23 * HOUR + 59 * MIN + 25_000;
        ledger
            .sample("sdc", d, 0, evening - 5_000, utc, PERIOD)
            .ok();
        ledger
            .sample("sdc", d, 1_000, evening, utc, PERIOD)
            .unwrap();
        let today = utc(evening).0;
        // 23:59:30: the day is posted with its 1_000 sectors.
        ledger.posted_day = Some(today);
        // 23:59:35 and 23:59:55: still today by the clock, but counted towards tomorrow.
        ledger
            .sample("sdc", d, 1_500, evening + 10_000, utc, PERIOD)
            .unwrap();
        ledger
            .sample("sdc", d, 2_000, evening + 30_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.disks["sdc"].day, today + 1);
        // After midnight the new day carries them — watched from its start, no comment.
        let next = evening + 35_000;
        ledger.sample("sdc", d, 2_100, next, utc, PERIOD).unwrap();
        assert_eq!(ledger.disks["sdc"].written.bytes, 1_100 * 512);
        assert_eq!(ledger.today("sdc", next, utc).unwrap().1, None);
        assert!(ledger.missed.is_empty(), "the posted day is not missed");
    }

    #[test]
    fn a_day_whose_post_window_was_missed_while_running_is_reported() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 23 * HOUR;
        ledger.sample("sdc", d, 0, t, utc, PERIOD).ok();
        ledger
            .sample("sdc", d, 2_000_000, t + 5_000, utc, PERIOD)
            .unwrap();
        // The host slept through midnight: no post; the next sample is tomorrow.
        assert_eq!(
            ledger.sample("sdc", d, 2_000_100, t + 2 * HOUR, utc, PERIOD),
            Err(Skip::GapAcrossDays)
        );
        // One entry per disk, both totals (the shorthand reads nothing: 0 GB read).
        assert_eq!(ledger.missed, vec![lost("sdc", utc(t).0, 1.024, 0.0)]);
    }

    #[test]
    fn a_day_that_ended_without_its_post_is_reported_once_at_start() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 9 * HOUR;
        ledger.sample("sdc", d, 0, t, utc, PERIOD).ok();
        ledger
            .sample("sdc", d, 2_000_000, t + 5_000, utc, PERIOD)
            .unwrap();
        let today = utc(t).0;
        let mut posted = ledger.clone();
        assert!(ledger.unposted_days(today).is_empty(), "still today");
        assert_eq!(
            ledger.unposted_days(today + 1),
            vec![lost("sdc", today, 1.024, 0.0)]
        );
        assert!(ledger.unposted_days(today + 1).is_empty(), "reported once");
        // …and not again as missed when the first sample of the next day turns it.
        assert_eq!(
            ledger.sample("sdc", d, 2_000_200, t + 24 * HOUR, utc, PERIOD),
            Err(Skip::GapAcrossDays)
        );
        assert!(ledger.missed.is_empty(), "{:?}", ledger.missed);
        assert_eq!(ledger.disks["sdc"].day, today + 1);
        posted.posted_day = Some(today);
        assert!(posted.unposted_days(today + 1).is_empty(), "posted");
        assert_eq!(day_label(today), "2026-09-29");
        assert_eq!(day_label(0), "1970-01-01");
    }

    #[test]
    fn the_read_total_mirrors_the_written_one_from_the_same_samples() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 10 * HOUR;
        assert_eq!(
            ledger.sample_counters("sdc", d, both(0, 0), t, utc, PERIOD),
            Err(Skip::Baseline)
        );
        assert_eq!(ledger.day_total("sdc", Kind::Read, t, utc), None);
        // 1 000 000 sectors written (0.512 GB) and 4 000 000 read (2.048 GB) in one sample.
        assert_eq!(
            ledger.sample_counters("sdc", d, both(1_000_000, 4_000_000), t + 5_000, utc, PERIOD),
            Ok(512_000_000)
        );
        assert_eq!(
            ledger.day_total("sdc", Kind::Written, t + 5_000, utc),
            Some((
                0.512,
                Some("measured since 10:00 local time (writes before that are not counted)".into())
            ))
        );
        assert_eq!(
            ledger.day_total("sdc", Kind::Read, t + 5_000, utc),
            Some((
                2.048,
                Some("measured since 10:00 local time (reads before that are not counted)".into())
            ))
        );
        // The read counter alone went backwards: only the read total re-bases, the written one
        // counts this sample.
        assert_eq!(
            ledger.sample_counters("sdc", d, both(1_000_100, 10), t + 10_000, utc, PERIOD),
            Ok(100 * 512)
        );
        assert_eq!(ledger.disks["sdc"].read.bytes, 2_048_000_000);
        ledger
            .sample_counters("sdc", d, both(1_000_100, 1_010), t + 15_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.disks["sdc"].read.bytes, 2_048_512_000);
        // A gap inside the day counts for both; midnight turns both, and a day that ends unposted
        // is one entry naming both totals.
        let evening = MIDNIGHT + 24 * HOUR - 3_000;
        ledger
            .sample_counters("sdc", d, both(1_000_100, 2_010), evening, utc, PERIOD)
            .unwrap();
        let after = evening + 5_000;
        ledger
            .sample_counters("sdc", d, both(1_000_200, 3_010), after, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.missed, vec![lost("sdc", utc(t).0, 0.512, 2.049)]);
        // The new day was watched from its start: no comment.
        assert_eq!(
            ledger.day_total("sdc", Kind::Read, after, utc),
            Some((0.001, None))
        );
        assert_eq!(
            ledger.day_total("sdc", Kind::Written, after, utc),
            Some((0.0, None))
        );
    }

    #[test]
    fn a_written_counter_reset_alone_still_counts_the_samples_reads() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 10 * HOUR;
        ledger
            .sample_counters("sdc", d, both(5_000, 0), t, utc, PERIOD)
            .ok();
        ledger
            .sample_counters("sdc", d, both(6_000, 1_000), t + 5_000, utc, PERIOD)
            .unwrap();
        // The written counter went backwards, the read one advanced 2 000 sectors: the written
        // total re-bases, the read total counts them.
        assert_eq!(
            ledger.sample_counters("sdc", d, both(100, 3_000), t + 10_000, utc, PERIOD),
            Err(Skip::CounterReset)
        );
        assert_eq!(ledger.disks["sdc"].written.bytes, 1_000 * 512);
        assert_eq!(ledger.disks["sdc"].read.bytes, 3_000 * 512);
        // Both counters continue from the reset sample.
        ledger
            .sample_counters("sdc", d, both(300, 3_500), t + 15_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.disks["sdc"].written.bytes, 1_200 * 512);
        assert_eq!(ledger.disks["sdc"].read.bytes, 3_500 * 512);
        // A written reset across a missed midnight drops the reads too (the gap rule is shared).
        let next_day = t + 24 * HOUR;
        assert_eq!(
            ledger.sample_counters("sdc", d, both(10, 9_000), next_day, utc, PERIOD),
            Err(Skip::CounterReset)
        );
        assert!(
            !ledger.disks["sdc"].read.measured,
            "{:?}",
            ledger.disks["sdc"]
        );
    }

    #[test]
    fn reads_after_the_days_post_count_towards_the_next_day() {
        let mut ledger = Ledger::default();
        let d = Some("wwid:sdc");
        let evening = MIDNIGHT + 23 * HOUR + 59 * MIN + 25_000;
        ledger
            .sample_counters("sdc", d, both(0, 0), evening - 5_000, utc, PERIOD)
            .ok();
        ledger
            .sample_counters("sdc", d, both(0, 1_000), evening, utc, PERIOD)
            .unwrap();
        let today = utc(evening).0;
        // 23:59:30: the day is posted with its 1_000 sectors read.
        ledger.posted_day = Some(today);
        ledger
            .sample_counters("sdc", d, both(0, 1_500), evening + 10_000, utc, PERIOD)
            .unwrap();
        ledger
            .sample_counters("sdc", d, both(0, 2_000), evening + 30_000, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.disks["sdc"].day, today + 1);
        let next = evening + 35_000;
        ledger
            .sample_counters("sdc", d, both(0, 2_100), next, utc, PERIOD)
            .unwrap();
        assert_eq!(ledger.disks["sdc"].read.bytes, 1_100 * 512);
        assert_eq!(
            ledger.day_total("sdc", Kind::Read, next, utc).unwrap().1,
            None
        );
        assert!(ledger.missed.is_empty(), "the posted day is not missed");
    }

    #[test]
    fn the_read_day_survives_a_restart_and_a_reboot_drops_only_its_counter() {
        let tree = FakeTree::new("disk-read");
        let path = tree.0.join(FILE_NAME);
        let logger = Logger::new(Level::Error, None);
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 9 * HOUR;
        let mut ledger = Ledger::default();
        ledger
            .sample_counters("sdc", d, both(0, 0), t, utc, PERIOD)
            .ok();
        ledger
            .sample_counters("sdc", d, both(0, 2_000_000), t + 5_000, utc, PERIOD)
            .unwrap();
        ledger.save(&path, "boot-a").unwrap();

        // Restarted in the same boot: the reads made while the probe was down count, once.
        let mut same = Ledger::load(&path, "boot-a", &logger);
        assert_eq!(same, ledger);
        same.sample_counters("sdc", d, both(0, 4_000_000), t + HOUR, utc, PERIOD)
            .unwrap();
        assert_eq!(
            same.day_total("sdc", Kind::Read, t + HOUR, utc).unwrap().0,
            2.048
        );
        same.save(&path, "boot-a").unwrap();
        let mut again = Ledger::load(&path, "boot-a", &logger);
        again
            .sample_counters("sdc", d, both(0, 4_000_000), t + HOUR + 5_000, utc, PERIOD)
            .unwrap();
        assert_eq!(
            again.day_total("sdc", Kind::Read, t + HOUR, utc).unwrap().0,
            2.048,
            "a second restart does not count the same reads twice"
        );

        // After a reboot the kernel counters restarted: a baseline for both, and the day is kept.
        let mut rebooted = Ledger::load(&path, "boot-b", &logger);
        assert_eq!(
            rebooted.sample_counters("sdc", d, both(10, 3_000_000), t + 2 * HOUR, utc, PERIOD),
            Err(Skip::Baseline)
        );
        assert_eq!(
            rebooted
                .day_total("sdc", Kind::Read, t + 2 * HOUR, utc)
                .unwrap()
                .0,
            2.048
        );
        rebooted
            .sample_counters(
                "sdc",
                d,
                both(10, 5_000_000),
                t + 2 * HOUR + 5_000,
                utc,
                PERIOD,
            )
            .unwrap();
        assert_eq!(
            rebooted
                .day_total("sdc", Kind::Read, t + 2 * HOUR, utc)
                .unwrap()
                .0,
            3.072
        );
    }

    /// The 0.7.0 ledger types, field for field: what an older probe reads.
    #[derive(Debug, Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct OldBaseline {
        sectors: u64,
        at_ms: i64,
        day: i64,
    }

    #[derive(Debug, Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct OldRecord {
        day: i64,
        bytes: u64,
        measured: bool,
        #[serde(default)]
        since_second: Option<i64>,
        #[serde(default)]
        baseline: Option<OldBaseline>,
        #[serde(default)]
        identity: Option<String>,
    }

    #[derive(Debug, Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct OldFile {
        version: u32,
        boot_id: String,
        disks: BTreeMap<String, OldRecord>,
        #[serde(default)]
        posted_day: Option<i64>,
    }

    #[test]
    fn a_ledger_of_0_7_0_loads_and_an_older_probe_reads_this_one() {
        let tree = FakeTree::new("disk-written-0.7.0");
        let path = tree.0.join(FILE_NAME);
        let logger = Logger::new(Level::Error, None);
        let d = Some("wwid:sdc");
        let t = MIDNIGHT + 9 * HOUR;
        let day = utc(t).0;
        // Saved by 0.7.0 at 09:00:05: 1.024 GB written since 09:00, no read fields.
        tree.file(
            FILE_NAME,
            &format!(
                r#"{{"version": 1, "bootId": "boot-a", "postedDay": {posted}, "disks": {{"sdc": {{
                    "day": {day}, "bytes": 1024000000, "measured": true, "sinceSecond": 32400,
                    "baseline": {{"sectors": 2000000, "atMs": {at}, "day": {day}}},
                    "identity": "wwid:sdc"}}}}}}"#,
                posted = day - 1,
                at = t + 5_000
            ),
        );
        let mut ledger = Ledger::load(&path, "boot-a", &logger);
        assert_eq!(ledger.posted_day, Some(day - 1));
        assert_eq!(ledger.disks["sdc"].read, Tally::default());
        // Upgraded at 10:00: the written day continues (the writes meanwhile count); the read day
        // only takes its baseline.
        let upgrade = t + HOUR;
        assert_eq!(
            ledger.sample_counters("sdc", d, both(4_000_000, 7_000_000), upgrade, utc, PERIOD),
            Ok(1_024_000_000)
        );
        assert_eq!(
            ledger.day_total("sdc", Kind::Written, upgrade, utc),
            Some((
                2.048,
                Some("measured since 09:00 local time (writes before that are not counted)".into())
            ))
        );
        assert_eq!(ledger.day_total("sdc", Kind::Read, upgrade, utc), None);
        // From the next sample on, the read day counts — measured since the upgrade.
        ledger
            .sample_counters(
                "sdc",
                d,
                both(4_000_000, 7_002_000),
                upgrade + 5_000,
                utc,
                PERIOD,
            )
            .unwrap();
        assert_eq!(
            ledger.day_total("sdc", Kind::Read, upgrade + 5_000, utc),
            Some((
                0.001,
                Some("measured since 10:00 local time (reads before that are not counted)".into())
            ))
        );

        // Saved by this probe: the written fields keep their 0.7.0 shape, and 0.7.0 reads the
        // file, ignoring the read ones.
        ledger.save(&path, "boot-a").unwrap();
        let text = fs::read_to_string(&path).unwrap();
        assert!(
            text.contains("\"read\"") && text.contains("\"readSectors\""),
            "{text}"
        );
        let old: OldFile = serde_json::from_str(&text).expect("an older probe loads it");
        assert_eq!(
            (old.version, old.boot_id.as_str()),
            (FILE_VERSION, "boot-a")
        );
        assert_eq!(old.posted_day, Some(day - 1));
        let sdc = &old.disks["sdc"];
        assert_eq!(
            (sdc.day, sdc.bytes, sdc.measured),
            (day, 2_048_000_000, true)
        );
        assert_eq!(sdc.since_second, Some(32_400));
        assert_eq!(sdc.identity.as_deref(), Some("wwid:sdc"));
        let baseline = sdc.baseline.as_ref().unwrap();
        assert_eq!(
            (baseline.sectors, baseline.at_ms, baseline.day),
            (4_000_000, upgrade + 5_000, day)
        );
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
