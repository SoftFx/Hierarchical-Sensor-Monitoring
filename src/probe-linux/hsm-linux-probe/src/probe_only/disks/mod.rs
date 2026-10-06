//! Disk sensors for every mounted real filesystem (#1481): free space (MB and %), free inodes (%)
//! and the write and read speeds and daily written and read volumes of the disk underneath.
//!
//! # Layout — the Windows per-drive naming
//!
//! The managed/native Windows collectors name disk sensors per drive letter directly under
//! `.computer/Disks monitoring/` (`Free space on C disk`, `Average disk write speed on C disk`).
//! Linux has no letters, so each filesystem gets a name ([`mounts`]: `root` for `/`, else the last
//! mount-path segment) and the same shape:
//!
//! | Sensor | Type · unit | Period |
//! |---|---|---|
//! | `Free space on <name> disk` | Double · MB (whole MB, like the managed sensor) | 5 min |
//! | `Free space on <name> disk %` | Double · Percents | 5 min |
//! | `Free inodes on <name> disk %` | Double · Percents | 5 min |
//! | `Average disk write speed on <name> disk` | DoubleBar · MBytes_sec | 5 s samples, 5-min bar |
//! | `Average disk read speed on <name> disk` | DoubleBar · MBytes_sec | the same samples and bar |
//! | `Written per day on <name> disk` | Double · GB (decimal) | 5 s samples, posted once a day |
//! | `Read per day on <name> disk` | Double · GB (decimal) | the same samples, posted with it |
//!
//! Every 5-minute sensor carries a 15-minute TTL (three periods): a filesystem that stops being
//! sampled — unmounted, or its `statvfs` hanging — turns to Timeout on the server. `Written per
//! day` and `Read per day` carry 26 hours, so a day without its post shows.
//! The shared collector's `Disks monitoring/Free space on disk` (+ `prediction`) is the managed
//! Unix parity sensor on `/` and is not touched.
//!
//! # What is read — and what never is
//!
//! `/proc/self/mountinfo` (which filesystems), `statvfs(2)` of each reported mount point, sysfs
//! `uevent` files and `/proc/diskstats`. Nothing under a mount is ever opened, listed or read:
//! `statvfs` is answered from the filesystem's superblock. Measured on garage-server
//! (2026-09-29): `statvfs` on the sleeping FUSE-NTFS archives and on an ext4 partition of a
//! sleeping disk left both disks in standby (`smartctl -n standby`, before / 5 s / 35 s after).
//!
//! # Isolation
//!
//! Space and write speed are two sources on two threads. Every `statvfs` runs on a helper thread
//! with a 5 s deadline, and a mount whose previous `statvfs` is still blocked is not asked again —
//! so a hung FUSE daemon costs one parked thread, not the other disks' samples. Failures are
//! logged once per filesystem until they recover (root rule #8), never posted as a value.
//!
//! # Mounts that come and go
//!
//! The set is resolved before the collector starts and re-scanned every 10 minutes. A new
//! filesystem registers its sensors while the collector runs (collector ≥ 0.9.1 re-posts the
//! registration, alerts included); one that disappears stops reporting (its sensors time out) and
//! resumes under the same name if it comes back. Names, once given, never change: the mount point
//! → name map is persisted ([`names`]), so they survive restarts too.

pub mod diskstats;
pub mod mounts;
pub mod names;
pub mod written;

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use hsm_collector::{
    instant_hourly_schedule_anchor, Alert, AlertCombination, AlertDestination, AlertIcon,
    AlertKind, AlertOperation, AlertProperty, AlertRepeat, AlertTarget, Collector, DoubleBarSensor,
    DoubleSensor, SensorOptions, STATISTICS_EMA,
};

use self::diskstats::{Skip, WriteRate};
use self::mounts::Filesystem;
use self::written::{Kind, Ledger, LocalTime};
use super::{FailureLog, HostEnvironment, Source};
use crate::config::DisksConfig;
use crate::logging::{Level, Logger};

/// Free space / inodes posting period.
pub const SPACE_PERIOD: Duration = Duration::from_secs(300);
/// How often the mount table is re-read for filesystems that appeared or went away.
pub const RESCAN_PERIOD: Duration = Duration::from_secs(600);
/// The one `/proc/diskstats` sampling period (the managed `BarTickPeriod`): every disk counter —
/// the write-speed bar (5 minutes), `Written per day` and `Read per day` — comes from these reads.
pub const DISK_SAMPLE_PERIOD: Duration = Duration::from_secs(5);
const WRITE_BAR_PERIOD: Duration = Duration::from_secs(300);
/// Carried by the ABI for parity with the managed `PostDataPeriod`.
const WRITE_BAR_POST_PERIOD: Duration = Duration::from_secs(15);
const WRITE_BAR_PRECISION: i32 = 2;
/// `Written per day` and `Read per day` are posted in the day's last this many sample periods
/// (their only post).
const FINAL_READING_PERIODS: i64 = 6;
/// The per-day sensors' TTL: a day and two hours, so one missed day turns them to Timeout.
const PER_DAY_TTL: Duration = Duration::from_secs(26 * 3600);
/// Three periods of the 5-minute sensors.
const TTL: Duration = Duration::from_secs(3 * 300);
/// How long one `statvfs` may take before that filesystem's sample is given up.
const STATVFS_TIMEOUT: Duration = if cfg!(test) {
    Duration::from_millis(300)
} else {
    Duration::from_secs(5)
};

/// Codes of the managed `Unit` enum.
const UNIT_MB: i32 = 3;
const UNIT_GB: i32 = 4;
const UNIT_PERCENTS: i32 = 100;
const UNIT_MBYTES_SEC: i32 = 2103;

pub const CATEGORY: &str = ".computer/Disks monitoring";

pub fn free_space_path(name: &str) -> String {
    format!("{CATEGORY}/Free space on {name} disk")
}
pub fn free_space_percent_path(name: &str) -> String {
    format!("{CATEGORY}/Free space on {name} disk %")
}
pub fn free_inodes_percent_path(name: &str) -> String {
    format!("{CATEGORY}/Free inodes on {name} disk %")
}
pub fn write_speed_path(name: &str) -> String {
    format!("{CATEGORY}/Average disk write speed on {name} disk")
}
pub fn read_speed_path(name: &str) -> String {
    format!("{CATEGORY}/Average disk read speed on {name} disk")
}
pub fn written_per_day_path(name: &str) -> String {
    format!("{CATEGORY}/Written per day on {name} disk")
}
pub fn read_per_day_path(name: &str) -> String {
    format!("{CATEGORY}/Read per day on {name} disk")
}

/// The `statvfs` fields the sensors use.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct FsStats {
    /// `f_frsize`: the unit of the block counts.
    pub fragment_size: u64,
    pub blocks: u64,
    pub blocks_available: u64,
    pub files: u64,
    pub files_available: u64,
}

impl FsStats {
    /// `f_bavail × f_frsize` in whole MB (1024²), what an unprivileged writer can still use.
    pub fn free_megabytes(&self) -> f64 {
        (self.blocks_available.saturating_mul(self.fragment_size) / (1024 * 1024)) as f64
    }

    /// `f_bavail / f_blocks` in percent (what `df` shows a non-root user).
    pub fn free_space_percent(&self) -> Option<f64> {
        percent(self.blocks_available, self.blocks)
    }

    /// `f_favail / f_files` in percent.
    pub fn free_inodes_percent(&self) -> Option<f64> {
        percent(self.files_available, self.files)
    }
}

fn percent(part: u64, whole: u64) -> Option<f64> {
    if whole == 0 {
        return None;
    }
    let value = part as f64 * 100.0 / whole as f64;
    Some((value * 100.0).round() / 100.0)
}

pub fn statvfs(path: &Path) -> std::io::Result<FsStats> {
    #[cfg(unix)]
    {
        use std::os::unix::ffi::OsStrExt;
        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes())
            .map_err(|_| std::io::Error::from(std::io::ErrorKind::InvalidInput))?;
        let mut stats = std::mem::MaybeUninit::<libc::statvfs>::uninit();
        // SAFETY: a valid NUL-terminated path and a correctly sized out-buffer.
        let code = unsafe { libc::statvfs(c_path.as_ptr(), stats.as_mut_ptr()) };
        if code != 0 {
            return Err(std::io::Error::last_os_error());
        }
        // SAFETY: statvfs returned 0, so it filled the struct.
        let stats = unsafe { stats.assume_init() };
        // The field types differ between libc targets (u32 on some 32-bit ABIs), hence `as`.
        #[allow(clippy::unnecessary_cast)]
        Ok(FsStats {
            fragment_size: stats.f_frsize as u64,
            blocks: stats.f_blocks as u64,
            blocks_available: stats.f_bavail as u64,
            files: stats.f_files as u64,
            files_available: stats.f_favail as u64,
        })
    }
    #[cfg(not(unix))]
    {
        let _ = path;
        Err(std::io::Error::new(
            std::io::ErrorKind::Unsupported,
            "statvfs is Linux-only",
        ))
    }
}

type StatvfsFn = fn(&Path) -> std::io::Result<FsStats>;

/// `statvfs` on a helper thread, bounded by [`STATVFS_TIMEOUT`]. `in_flight` is set while a call
/// is outstanding: a filesystem whose last `statvfs` never returned is not asked again, so a hung
/// mount parks one thread, not one per sample.
fn statvfs_bounded(
    path: &Path,
    statvfs: StatvfsFn,
    in_flight: &Arc<AtomicBool>,
) -> Result<FsStats, String> {
    if in_flight.swap(true, Ordering::SeqCst) {
        return Err(format!(
            "statvfs({}) from an earlier sample has still not returned (a hung filesystem?)",
            path.display()
        ));
    }
    let (tx, rx) = std::sync::mpsc::channel();
    let (flag, owned) = (Arc::clone(in_flight), path.to_path_buf());
    let spawned = std::thread::Builder::new()
        .name("probe-statvfs".into())
        .spawn(move || {
            let result = statvfs(&owned);
            flag.store(false, Ordering::SeqCst);
            let _ = tx.send(result);
        });
    if let Err(error) = spawned {
        in_flight.store(false, Ordering::SeqCst);
        return Err(format!("cannot start a statvfs thread: {error}"));
    }
    match rx.recv_timeout(STATVFS_TIMEOUT) {
        Ok(Ok(stats)) => Ok(stats),
        Ok(Err(error)) => Err(format!("statvfs({}) failed: {error}", path.display())),
        Err(_) => Err(format!(
            "statvfs({}) did not return within {} ms (a hung filesystem?)",
            path.display(),
            STATVFS_TIMEOUT.as_millis()
        )),
    }
}

/// One reported filesystem and its sensors.
struct Node<'c> {
    name: String,
    fs: Filesystem,
    /// The whole disk its write speed is read from (a `/proc/diskstats` name).
    disk: Option<String>,
    mounted: bool,
    free_mb: Option<DoubleSensor<'c>>,
    free_percent: Option<DoubleSensor<'c>>,
    free_inodes: Option<DoubleSensor<'c>>,
    /// Whether the filesystem counts inodes is not known yet (the discovery `statvfs` failed):
    /// decided — and the inode sensor registered or not — on the first successful sample.
    inodes_pending: bool,
    write_speed: Option<DoubleBarSensor<'c>>,
    /// Registered wherever the write speed is: the same disk, the same samples.
    read_speed: Option<DoubleBarSensor<'c>>,
    /// Registered wherever the write speed is: the same disk, the same counter.
    written_per_day: Option<DoubleSensor<'c>>,
    /// Registered wherever `Written per day` is: the same disk, the same samples.
    read_per_day: Option<DoubleSensor<'c>>,
    in_flight: Arc<AtomicBool>,
    failures: FailureLog,
}

/// What registration needs to know about a filesystem, found without holding any lock.
struct Discovered {
    fs: Filesystem,
    name: String,
    disk: Option<String>,
    /// `None` when the discovery `statvfs` failed: decided on the first successful sample.
    inodes_counted: Option<bool>,
    /// Carried into the node, so a `statvfs` left hanging by discovery is not repeated.
    in_flight: Arc<AtomicBool>,
}

/// Everything both disk sources share.
struct Disks<'c> {
    collector: &'c Collector,
    config: DisksConfig,
    sys_root: PathBuf,
    mountinfo: PathBuf,
    fstab: PathBuf,
    statvfs: StatvfsFn,
    /// Mount point → name for every filesystem ever named. Names are never reused or changed, and
    /// the map is persisted in `names_path`, so they survive restarts too.
    names: Mutex<BTreeMap<String, String>>,
    names_path: Option<PathBuf>,
    /// Hidden (over-mounted) filesystems already warned about, so each is logged once.
    hidden_reported: Mutex<BTreeSet<PathBuf>>,
    nodes: Mutex<Vec<Node<'c>>>,
}

impl<'c> Disks<'c> {
    /// The mount points already reported: kept while their device stays mounted there.
    fn mount_points(&self) -> BTreeSet<PathBuf> {
        let nodes = self.nodes.lock().unwrap_or_else(|p| p.into_inner());
        nodes
            .iter()
            .map(|node| node.fs.mount_point.clone())
            .collect()
    }

    /// Read the mount table and name what is new (persisting new names). `Err` when the table
    /// cannot be read.
    fn scan(&self, logger: &Logger) -> Result<Vec<(Filesystem, String)>, String> {
        let text = std::fs::read_to_string(&self.mountinfo)
            .map_err(|error| format!("cannot read {}: {error}", self.mountinfo.display()))?;
        let parsed = mounts::parse_mountinfo(&text);
        let filesystems =
            mounts::real_filesystems_keeping(&parsed, &self.config.exclude, &self.mount_points());
        {
            let mut reported = self
                .hidden_reported
                .lock()
                .unwrap_or_else(|p| p.into_inner());
            // /etc/fstab missing or unreadable: nothing to compare against, nothing to report.
            let fstab = std::fs::read_to_string(&self.fstab).unwrap_or_default();
            let hidden = mounts::hidden_filesystems(&parsed)
                .into_iter()
                .chain(mounts::fstab_hidden(&fstab, &parsed));
            for point in hidden {
                if reported.insert(point.clone()) {
                    logger.warn(format!(
                        "disks: the filesystem at {} is not visible in this service's namespace \
                         (hidden by ProtectHome=/InaccessiblePaths= or by another mount) and is \
                         not reported",
                        point.display()
                    ));
                }
            }
        }
        let mut names = self.names.lock().unwrap_or_else(|p| p.into_inner());
        let before = names.len();
        mounts::assign_names(&filesystems, &mut names);
        if names.len() != before {
            if let Some(path) = &self.names_path {
                if let Err(error) = names::save(path, &names) {
                    // Names stay stable for this run; only a restart could rename a filesystem
                    // whose natural name collides with one that appears later.
                    logger.error(format!(
                        "disks: cannot save the disk names to {}: {error}",
                        path.display()
                    ));
                }
            }
        }
        Ok(filesystems
            .into_iter()
            .map(|fs| {
                let name = names[&mounts::name_key(&fs)].clone();
                (fs, name)
            })
            .collect())
    }

    /// The facts registration needs: the disk underneath and whether the filesystem counts
    /// inodes (one bounded `statvfs`; if it fails, the first successful sample decides).
    fn discover(&self, fs: Filesystem, name: String) -> Discovered {
        let disk = if self.config.write_speed {
            diskstats::whole_disk(&self.sys_root, &fs.device, &fs.source)
        } else {
            None
        };
        let in_flight = Arc::new(AtomicBool::new(false));
        let inodes_counted = statvfs_bounded(&fs.mount_point, self.statvfs, &in_flight)
            .ok()
            .map(|stats| stats.files > 0);
        Discovered {
            fs,
            name,
            disk,
            inodes_counted,
            in_flight,
        }
    }

    /// Register the sensors of newly found filesystems (the nodes lock is held by the caller).
    fn register_nodes(&self, found: Vec<Discovered>, nodes: &mut Vec<Node<'c>>, logger: &Logger) {
        // Which names each disk carries, for the write-speed description.
        let mut by_disk: BTreeMap<String, BTreeSet<String>> = BTreeMap::new();
        for (disk, name) in nodes
            .iter()
            .map(|node| (node.disk.clone(), node.name.clone()))
            .chain(found.iter().map(|d| (d.disk.clone(), d.name.clone())))
        {
            if let Some(disk) = disk {
                by_disk.entry(disk).or_default().insert(name);
            }
        }
        for discovered in found {
            let node = register_node(self.collector, discovered, &by_disk, logger);
            logger.info(format!(
                "disks: '{}' = {} ({} {}{}){}",
                node.name,
                node.fs.mount_point.display(),
                node.fs.fs_type,
                node.fs.source,
                node.disk
                    .as_deref()
                    .map(|disk| format!(", disk {disk}"))
                    .unwrap_or_default(),
                if node.fs.mount_points.len() > 1 {
                    format!(
                        ", also mounted at {} other point(s)",
                        node.fs.mount_points.len() - 1
                    )
                } else {
                    String::new()
                }
            ));
            nodes.push(node);
        }
    }

    /// Re-read the mount table: register what is new, mark what went away or came back.
    fn rescan(&self, logger: &Logger, failures: &mut FailureLog) {
        let current = match self.scan(logger) {
            Ok(current) => {
                failures.succeeded(logger, "disks: mount table");
                current
            }
            Err(reason) => {
                failures.failed(logger, "disks: mount table", &reason);
                return;
            }
        };
        // Nodes are keyed like names: by mount point. A different device at a known mount point
        // (a USB stick swapped) continues that mount point's sensors; a filesystem moved to
        // another mount point reports under that mount point's name.
        let known: BTreeSet<String> = {
            let nodes = self.nodes.lock().unwrap_or_else(|p| p.into_inner());
            nodes
                .iter()
                .map(|node| mounts::name_key(&node.fs))
                .collect()
        };
        // Discovery touches the filesystem: done before taking the nodes lock.
        let found: Vec<Discovered> = current
            .iter()
            .filter(|(fs, _)| !known.contains(&mounts::name_key(fs)))
            .map(|(fs, name)| self.discover(fs.clone(), name.clone()))
            .collect();

        let mut nodes = self.nodes.lock().unwrap_or_else(|p| p.into_inner());
        let present: BTreeMap<String, &Filesystem> = current
            .iter()
            .map(|(fs, _)| (mounts::name_key(fs), fs))
            .collect();
        for node in nodes.iter_mut() {
            match present.get(&mounts::name_key(&node.fs)) {
                Some(fs) => {
                    if !node.mounted {
                        logger.info(format!(
                            "disks: '{}' is mounted again at {} ({})",
                            node.name,
                            fs.mount_point.display(),
                            fs.source
                        ));
                    }
                    let swapped = fs.device != node.fs.device;
                    node.mounted = true;
                    node.fs = (*fs).clone();
                    if swapped {
                        // Another device behind the same mount point: follow its disk, add the
                        // write-speed sensor if the first device had none, and let the next good
                        // sample decide the inode sensor again. (Descriptions keep the first
                        // device's text; registrations cannot be removed.)
                        node.inodes_pending = true;
                        if self.config.write_speed {
                            node.disk =
                                diskstats::whole_disk(&self.sys_root, &fs.device, &fs.source);
                            if let Some(disk) = node.disk.clone() {
                                if node.write_speed.is_none() {
                                    node.write_speed = register_write_speed(
                                        self.collector,
                                        logger,
                                        &node.name,
                                        &node.fs,
                                        &disk,
                                        &[],
                                    );
                                }
                                if node.read_speed.is_none() {
                                    node.read_speed = register_read_speed(
                                        self.collector,
                                        logger,
                                        &node.name,
                                        &node.fs,
                                        &disk,
                                        &[],
                                    );
                                }
                                if node.written_per_day.is_none() {
                                    node.written_per_day = register_written_per_day(
                                        self.collector,
                                        logger,
                                        &node.name,
                                        &node.fs,
                                        &disk,
                                        &[],
                                    );
                                }
                                if node.read_per_day.is_none() {
                                    node.read_per_day = register_read_per_day(
                                        self.collector,
                                        logger,
                                        &node.name,
                                        &node.fs,
                                        &disk,
                                        &[],
                                    );
                                }
                            }
                        }
                    }
                }
                None if node.mounted => {
                    node.mounted = false;
                    logger.info(format!(
                        "disks: '{}' ({}) is no longer mounted; its sensors stop reporting and \
                         time out",
                        node.name,
                        node.fs.mount_point.display()
                    ));
                }
                None => {}
            }
        }
        self.register_nodes(found, &mut nodes, logger);
    }
}

fn register_node<'c>(
    collector: &'c Collector,
    discovered: Discovered,
    by_disk: &BTreeMap<String, BTreeSet<String>>,
    logger: &Logger,
) -> Node<'c> {
    let Discovered {
        fs,
        name,
        disk,
        inodes_counted,
        in_flight,
    } = discovered;
    let where_ = where_(&fs);
    let options = |unit: i32, description: String| {
        SensorOptions::default()
            .with_is_computer_sensor(true)
            .with_ttl(TTL)
            .with_unit(unit)
            .with_description(description)
    };

    let free_mb = register_double(
        collector,
        logger,
        &free_space_path(&name),
        options(UNIT_MB, format!(
            "Free space on {where_}, in MB: statvfs f_bavail × f_frsize, what `df` shows a non-root \
             user. Every 5 minutes."
        ))
        .with_statistics(STATISTICS_EMA),
        // No absolute-size alert: the managed 20 GB threshold would hold a /boot/efi of 512 MB in
        // Error forever. The percent sensor carries the alerts, which scale with the filesystem;
        // `/` keeps the 20 GB alert on the managed-parity `Free space on disk`.
        Vec::new(),
    );
    let free_percent = register_double(
        collector,
        logger,
        &free_space_percent_path(&name),
        options(
            UNIT_PERCENTS,
            format!(
                "Free space on {where_}, in percent: statvfs f_bavail / f_blocks. Every 5 minutes."
            ),
        ),
        vec![
            percent_alert(collector, Band::Warning, "10", Some("5")),
            percent_alert(collector, Band::Error, "5", None),
        ],
    );
    let free_inodes = match inodes_counted {
        Some(true) => register_inodes(collector, logger, &name, &fs),
        Some(false) => {
            log_no_inodes(logger, &name, &fs);
            None
        }
        // The discovery statvfs failed: the first successful sample decides.
        None => None,
    };
    let sharing = disk
        .as_ref()
        .and_then(|disk| by_disk.get(disk))
        .map(|names| {
            names
                .iter()
                .filter(|other| **other != name)
                .cloned()
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    let write_speed = disk
        .as_ref()
        .and_then(|disk| register_write_speed(collector, logger, &name, &fs, disk, &sharing));
    let read_speed = disk
        .as_ref()
        .and_then(|disk| register_read_speed(collector, logger, &name, &fs, disk, &sharing));
    let written_per_day = disk
        .as_ref()
        .and_then(|disk| register_written_per_day(collector, logger, &name, &fs, disk, &sharing));
    let read_per_day = disk
        .as_ref()
        .and_then(|disk| register_read_per_day(collector, logger, &name, &fs, disk, &sharing));
    Node {
        name,
        fs,
        disk,
        mounted: true,
        free_mb,
        free_percent,
        free_inodes,
        inodes_pending: inodes_counted.is_none(),
        write_speed,
        read_speed,
        written_per_day,
        read_per_day,
        in_flight,
        failures: FailureLog::default(),
    }
}

fn where_(fs: &Filesystem) -> String {
    format!(
        "the {} filesystem {} mounted at {}",
        fs.fs_type,
        fs.source,
        fs.mount_point.display()
    )
}

fn register_inodes<'c>(
    collector: &'c Collector,
    logger: &Logger,
    name: &str,
    fs: &Filesystem,
) -> Option<DoubleSensor<'c>> {
    register_double(
        collector,
        logger,
        &free_inodes_percent_path(name),
        SensorOptions::default()
            .with_is_computer_sensor(true)
            .with_ttl(TTL)
            .with_unit(UNIT_PERCENTS)
            .with_description(format!(
                "Free inodes on {}, in percent: statvfs f_favail / f_files. Running out blocks \
                 file creation while space is still free. Every 5 minutes.",
                where_(fs)
            )),
        vec![percent_alert(collector, Band::Warning, "10", None)],
    )
}

fn register_write_speed<'c>(
    collector: &'c Collector,
    logger: &Logger,
    name: &str,
    fs: &Filesystem,
    disk: &str,
    sharing: &[String],
) -> Option<DoubleBarSensor<'c>> {
    let shared = if sharing.is_empty() {
        String::new()
    } else {
        format!(
            " The disk is shared, so this is also the write speed of: {}.",
            sharing.join(", ")
        )
    };
    let options = SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_ttl(TTL)
        .with_unit(UNIT_MBYTES_SEC)
        .with_statistics(STATISTICS_EMA)
        .with_description(format!(
            "Average write speed of the whole disk {disk} under {}, in MB/s: /proc/diskstats \
             sectors written, one sample every 5 s into a 5-minute bar.{shared}",
            where_(fs)
        ));
    let path = write_speed_path(name);
    match collector.double_bar_sensor(
        &path,
        WRITE_BAR_PERIOD,
        WRITE_BAR_POST_PERIOD,
        WRITE_BAR_PRECISION,
        &options,
    ) {
        Ok(sensor) => Some(sensor),
        Err(error) => {
            logger.error(format!("cannot register {path}: {error}"));
            None
        }
    }
}

/// The mirror of [`register_write_speed`] for "sectors read" (#1506): the same bar, unit, EMA and
/// TTL, from the same samples.
fn register_read_speed<'c>(
    collector: &'c Collector,
    logger: &Logger,
    name: &str,
    fs: &Filesystem,
    disk: &str,
    sharing: &[String],
) -> Option<DoubleBarSensor<'c>> {
    let shared = if sharing.is_empty() {
        String::new()
    } else {
        format!(
            " The disk is shared, so this is also the read speed of: {}.",
            sharing.join(", ")
        )
    };
    let options = SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_ttl(TTL)
        .with_unit(UNIT_MBYTES_SEC)
        .with_statistics(STATISTICS_EMA)
        .with_description(format!(
            "Average read speed of the whole disk {disk} under {}, in MB/s: /proc/diskstats \
             sectors read, from the write speed's samples (one every 5 s) into a 5-minute bar. \
             Kernel counters only: a sleeping disk is not woken.{shared}",
            where_(fs)
        ));
    let path = read_speed_path(name);
    match collector.double_bar_sensor(
        &path,
        WRITE_BAR_PERIOD,
        WRITE_BAR_POST_PERIOD,
        WRITE_BAR_PRECISION,
        &options,
    ) {
        Ok(sensor) => Some(sensor),
        Err(error) => {
            logger.error(format!("cannot register {path}: {error}"));
            None
        }
    }
}

/// No alert and no statistics (owner decision): one total per day, read as is.
fn register_written_per_day<'c>(
    collector: &'c Collector,
    logger: &Logger,
    name: &str,
    fs: &Filesystem,
    disk: &str,
    sharing: &[String],
) -> Option<DoubleSensor<'c>> {
    let shared = if sharing.is_empty() {
        String::new()
    } else {
        format!(
            " The disk is shared, so this is also the volume written per day of: {}.",
            sharing.join(", ")
        )
    };
    register_double(
        collector,
        logger,
        &written_per_day_path(name),
        SensorOptions::default()
            .with_is_computer_sensor(true)
            .with_ttl(PER_DAY_TTL)
            .with_unit(UNIT_GB)
            .with_description(format!(
                "Written to the whole disk {disk} under {} during one local day (the host's \
                 timezone), in GB (decimal: 1 GB = 10⁹ bytes, the unit disk endurance is rated \
                 in): /proc/diskstats sectors written, summed from the 5-s write-speed samples. \
                 **One value per day**, posted in the day's last 30 seconds: the total written \
                 from the previous day's post (about 23:59:30) to this one — what is written \
                 between a post and midnight counts towards the next day. The first sample or a \
                 counter reset only sets a baseline; a day with no \
                 measurement is not posted (never an invented 0), and a day whose measurement \
                 began after midnight says since when in the comment. A probe restart continues \
                 the day; the writes during a reboot are not counted; a day that ended while the \
                 probe was not running is not posted. TTL 26 h: a missing day shows as \
                 Timeout.{shared}",
                where_(fs)
            )),
        Vec::new(),
    )
}

/// The mirror of [`register_written_per_day`] for the bytes read (#1506): the same day, unit,
/// TTL and post; no alert and no statistics.
fn register_read_per_day<'c>(
    collector: &'c Collector,
    logger: &Logger,
    name: &str,
    fs: &Filesystem,
    disk: &str,
    sharing: &[String],
) -> Option<DoubleSensor<'c>> {
    let shared = if sharing.is_empty() {
        String::new()
    } else {
        format!(
            " The disk is shared, so this is also the volume read per day of: {}.",
            sharing.join(", ")
        )
    };
    register_double(
        collector,
        logger,
        &read_per_day_path(name),
        SensorOptions::default()
            .with_is_computer_sensor(true)
            .with_ttl(PER_DAY_TTL)
            .with_unit(UNIT_GB)
            .with_description(format!(
                "Read from the whole disk {disk} under {} during one local day (the host's \
                 timezone), in GB (decimal: 1 GB = 10⁹ bytes): /proc/diskstats sectors read, \
                 summed from the same 5-s samples as the write speed (kernel counters only: the \
                 disk is never asked, so a sleeping disk stays asleep). **One value per day**, \
                 posted with Written per day in the day's last 30 seconds: the total read from \
                 the previous day's post (about 23:59:30) to this one — what is read between a \
                 post and midnight counts towards the next day. The first sample or a counter \
                 reset only sets a baseline; a day with no measurement is not posted (never an \
                 invented 0), and a day whose measurement began after midnight says since when \
                 in the comment. A probe restart continues the day; the reads during a reboot \
                 are not counted; a day that ended while the probe was not running is not \
                 posted. TTL 26 h: a missing day shows as Timeout.{shared}",
                where_(fs)
            )),
        Vec::new(),
    )
}

fn log_no_inodes(logger: &Logger, name: &str, fs: &Filesystem) {
    logger.info(format!(
        "disks: '{name}' ({} {}) reports no inode count; no inode sensor",
        fs.mount_point.display(),
        fs.fs_type
    ));
}

fn register_double<'c>(
    collector: &'c Collector,
    logger: &Logger,
    path: &str,
    options: SensorOptions,
    alerts: Vec<hsm_collector::Result<Alert<'c>>>,
) -> Option<DoubleSensor<'c>> {
    let sensor = match collector.double_sensor(path, &options) {
        Ok(sensor) => sensor,
        Err(error) => {
            logger.error(format!("cannot register {path}: {error}"));
            return None;
        }
    };
    for alert in alerts {
        if let Err(error) = alert.and_then(|alert| sensor.attach_alert(&alert)) {
            logger.error(format!("cannot attach an alert to {path}: {error}"));
        }
    }
    Some(sensor)
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum Band {
    Warning,
    Error,
}

/// The #1476 percent alerts: fires while `value < below` (and `value >= not_below`, so the warning
/// stays quiet once the error band takes over). Warning = notification + ⚠, Error also sets the
/// sensor to Error.
fn percent_alert<'c>(
    collector: &'c Collector,
    band: Band,
    below: &str,
    not_below: Option<&str>,
) -> hsm_collector::Result<Alert<'c>> {
    let mut alert = collector.alert(AlertKind::Instant)?.condition(
        AlertCombination::And,
        AlertProperty::Value,
        AlertOperation::LessThan,
        AlertTarget::Const(below.to_string()),
    )?;
    if let Some(floor) = not_below {
        alert = alert.condition(
            AlertCombination::And,
            AlertProperty::Value,
            AlertOperation::GreaterThanOrEqual,
            AlertTarget::Const(floor.to_string()),
        )?;
    }
    alert = alert.scheduled_notification(
        "[$product]$path $property $operation $target$unit",
        instant_hourly_schedule_anchor(),
        AlertRepeat::Hourly,
        true,
        AlertDestination::FromParent,
    )?;
    Ok(match band {
        Band::Warning => alert.icon(AlertIcon::Warning)?.build(),
        Band::Error => alert.icon(AlertIcon::ArrowDown)?.sensor_error()?.build(),
    })
}

/// Register the disk sensors of every real filesystem (before Start) and return the space and
/// write-speed sources. The caller decides whether the disks are enabled
/// (`ProbeConfig::disks_enabled`).
pub fn register<'c>(
    collector: &'c Collector,
    config: &DisksConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    let (space, write) = build(collector, config, environment, logger);
    let mut sources: Vec<Box<dyn Source + 'c>> = vec![Box::new(space)];
    if let Some(write) = write {
        sources.push(Box::new(write));
    }
    sources
}

/// [`register`] with the concrete sources.
fn build<'c>(
    collector: &'c Collector,
    config: &DisksConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> (SpaceSource<'c>, Option<WriteSpeedSource<'c>>) {
    let disks = Arc::new(Disks {
        collector,
        config: config.clone(),
        sys_root: environment.sys_root.clone(),
        mountinfo: environment.mountinfo.clone(),
        fstab: environment.fstab.clone(),
        statvfs: environment.statvfs,
        names: Mutex::new(
            environment
                .disk_names
                .as_deref()
                .map(|path| names::load(path, logger))
                .unwrap_or_default(),
        ),
        names_path: environment.disk_names.clone(),
        hidden_reported: Mutex::new(BTreeSet::new()),
        nodes: Mutex::new(Vec::new()),
    });
    let mut failures = FailureLog::default();
    match disks.scan(logger) {
        Ok(current) => {
            let found: Vec<Discovered> = current
                .into_iter()
                .map(|(fs, name)| disks.discover(fs, name))
                .collect();
            let mut nodes = disks.nodes.lock().unwrap_or_else(|p| p.into_inner());
            disks.register_nodes(found, &mut nodes, logger);
            logger.info(format!(
                "disks: {} filesystem(s) registered; re-scanned every {} min",
                nodes.len(),
                RESCAN_PERIOD.as_secs() / 60
            ));
        }
        // Retried by the space source's re-scan.
        Err(reason) => failures.failed(logger, "disks: mount table", &reason),
    }

    let space = SpaceSource {
        disks: Arc::clone(&disks),
        last_scan: Instant::now(),
        scan_failures: failures,
        live_failures: FailureLog::default(),
    };
    let write = if config.write_speed {
        let boot_id = written::read_boot_id(&environment.boot_id);
        let ledger = environment
            .disk_written
            .as_deref()
            .map(|path| Ledger::load(path, &boot_id, logger))
            .unwrap_or_default();
        Some(WriteSpeedSource {
            disks,
            diskstats: environment.diskstats.clone(),
            rates: BTreeMap::new(),
            read_rates: BTreeMap::new(),
            failures: FailureLog::default(),
            ledger,
            ledger_path: environment.disk_written.clone(),
            boot_id,
            ledger_dirty: false,
            last_save: Instant::now(),
            #[cfg(test)]
            posts: 0,
            clock: unix_now_ms,
            local: written::local_time,
            post: post_per_day,
            unposted_pending: true,
            save_failures: FailureLog::default(),
            identities: BTreeMap::new(),
        })
    } else {
        logger.info("disks: write speed disabled (probe.disks.writeSpeed = false)");
        None
    };
    (space, write)
}

/// Free space and inodes every 5 minutes, and the 10-minute mount re-scan.
struct SpaceSource<'c> {
    disks: Arc<Disks<'c>>,
    last_scan: Instant,
    scan_failures: FailureLog,
    /// The per-sample mount-table read, logged separately from the re-scan.
    live_failures: FailureLog,
}

impl Source for SpaceSource<'_> {
    fn name(&self) -> &'static str {
        "disk space"
    }

    fn period(&self) -> Duration {
        SPACE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        // Every second sample (10 min): half a period of slack absorbs scheduling jitter.
        if self.last_scan.elapsed() + SPACE_PERIOD / 2 >= RESCAN_PERIOD {
            self.last_scan = Instant::now();
            self.disks.rescan(logger, &mut self.scan_failures);
        }

        // Only statvfs what is mounted right now: an unmounted mount point would answer for the
        // filesystem underneath it (posting `/`'s numbers under another name), and an automount
        // point would mount itself again. Checked on every sample, not only at the 10-min re-scan.
        let what = "disks: mount table (per sample)";
        let live: BTreeMap<PathBuf, String> = match std::fs::read_to_string(&self.disks.mountinfo) {
            Ok(text) => {
                self.live_failures.succeeded(logger, what);
                mounts::real_filesystems_keeping(
                    &mounts::parse_mountinfo(&text),
                    &self.disks.config.exclude,
                    &self.disks.mount_points(),
                )
                .into_iter()
                .map(|fs| (fs.mount_point, fs.device))
                .collect()
            }
            Err(error) => {
                self.live_failures.failed(
                    logger,
                    what,
                    &format!(
                        "cannot read {}: {error}; nothing sampled",
                        self.disks.mountinfo.display()
                    ),
                );
                return;
            }
        };

        // statvfs runs without the nodes lock held, so the write-speed thread is never held up.
        let targets: Vec<(usize, PathBuf, Arc<AtomicBool>)> = {
            let mut nodes = self.disks.nodes.lock().unwrap_or_else(|p| p.into_inner());
            for node in nodes.iter_mut() {
                match live.get(&node.fs.mount_point) {
                    None if node.mounted => {
                        node.mounted = false;
                        logger.info(format!(
                            "disks: '{}' ({}) is no longer mounted; its sensors stop reporting \
                             and time out",
                            node.name,
                            node.fs.mount_point.display()
                        ));
                    }
                    // Back (a quick replug) with the same device: resume now, not at the next
                    // re-scan. Another device behind the mount point is the re-scan's to adopt.
                    Some(device) if !node.mounted && *device == node.fs.device => {
                        node.mounted = true;
                        logger.info(format!(
                            "disks: '{}' is mounted again at {}",
                            node.name,
                            node.fs.mount_point.display()
                        ));
                    }
                    _ => {}
                }
            }
            nodes
                .iter()
                .enumerate()
                .filter(|(_, node)| node.mounted)
                .map(|(index, node)| {
                    (
                        index,
                        node.fs.mount_point.clone(),
                        Arc::clone(&node.in_flight),
                    )
                })
                .collect()
        };
        let results: Vec<(usize, Result<FsStats, String>)> = targets
            .into_iter()
            .map(|(index, path, flag)| (index, statvfs_bounded(&path, self.disks.statvfs, &flag)))
            .collect();

        let mut nodes = self.disks.nodes.lock().unwrap_or_else(|p| p.into_inner());
        for (index, result) in results {
            let node = &mut nodes[index];
            let what = format!("disks: '{}'", node.name);
            match result {
                Ok(stats) => {
                    node.failures.succeeded(logger, &what);
                    if node.inodes_pending {
                        // The discovery statvfs failed, or another device took the mount point:
                        // this first good answer decides.
                        node.inodes_pending = false;
                        match (stats.files > 0, node.free_inodes.is_some()) {
                            (true, false) => {
                                node.free_inodes = register_inodes(
                                    self.disks.collector,
                                    logger,
                                    &node.name,
                                    &node.fs,
                                );
                            }
                            (false, false) => log_no_inodes(logger, &node.name, &node.fs),
                            (false, true) => logger.info(format!(
                                "disks: '{}' now reports no inode count; its inode sensor stops \
                                 reporting and times out",
                                node.name
                            )),
                            (true, true) => {}
                        }
                    }
                    post(&node.free_mb, Some(stats.free_megabytes()), logger);
                    post(&node.free_percent, stats.free_space_percent(), logger);
                    post(&node.free_inodes, stats.free_inodes_percent(), logger);
                }
                Err(reason) => node.failures.failed(logger, &what, &reason),
            }
        }
    }
}

fn post(sensor: &Option<DoubleSensor<'_>>, value: Option<f64>, logger: &Logger) {
    if let (Some(sensor), Some(value)) = (sensor, value) {
        if let Err(error) = sensor.add(value) {
            logger.error(format!("disks: cannot post a value: {error}"));
        }
    }
}

/// Disk write speed: one `/proc/diskstats` read every 5 s, one rate per whole disk, posted into the
/// bar of every mounted filesystem on that disk. The same read feeds each disk's written and read
/// per-day totals ([`written`]), posted once a day.
struct WriteSpeedSource<'c> {
    disks: Arc<Disks<'c>>,
    diskstats: PathBuf,
    rates: BTreeMap<String, WriteRate>,
    /// The read-speed rates: the same arithmetic on "sectors read".
    read_rates: BTreeMap<String, WriteRate>,
    failures: FailureLog,
    ledger: Ledger,
    /// `$STATE_DIRECTORY/disk-written.json`; `None` keeps the day in memory only.
    ledger_path: Option<PathBuf>,
    boot_id: String,
    /// The ledger changed since it was last saved.
    ledger_dirty: bool,
    /// When the ledger was last saved (every 5 minutes, at the day's post and on stop).
    last_save: Instant,
    /// Values handed to the collector (tests: exactly one per sensor per day).
    #[cfg(test)]
    posts: usize,
    /// The wall clock, Unix milliseconds, and the host's local calendar (seams for tests).
    clock: fn() -> i64,
    local: LocalTime,
    /// Hands one `Written per day` or `Read per day` value to the collector (a seam for tests).
    post: PostFn,
    /// The days that ended while the probe was down are still to be reported (at the first
    /// sample, on the source's own clock).
    unposted_pending: bool,
    save_failures: FailureLog,
    /// Each disk's WWID/serial, read once while its name stays in `/proc/diskstats`.
    identities: BTreeMap<String, Option<String>>,
}

impl WriteSpeedSource<'_> {
    /// Post every filesystem's `Written per day` and `Read per day` (its disk's day totals) — the
    /// day's only post — and mark `day` posted when a value went out, or there was nothing to
    /// post: if every post failed, the next sample in the window tries again. What a marked day
    /// leaves unposted is logged with the disk, the day and the total (root rule #8): a value
    /// whose post failed, and a measured disk that no mounted filesystem with a sensor posts (one
    /// line naming both totals). The caller saves the ledger after releasing the nodes lock.
    /// Returns `(attempted, sent)`.
    fn post_per_day_totals(
        &mut self,
        nodes: &[Node<'_>],
        day: i64,
        logger: &Logger,
    ) -> (usize, usize) {
        let (mut attempted, mut sent) = (0, 0);
        let now_ms = (self.clock)();
        let label = written::day_label(day);
        let mut offered: BTreeSet<(&str, Kind)> = BTreeSet::new();
        let mut failed = Vec::new();
        for node in nodes.iter().filter(|node| node.mounted) {
            let Some(disk) = node.disk.as_deref() else {
                continue;
            };
            for (kind, sensor) in [
                (Kind::Written, &node.written_per_day),
                (Kind::Read, &node.read_per_day),
            ] {
                let Some(sensor) = sensor else {
                    continue;
                };
                let Some((gigabytes, comment)) =
                    self.ledger.day_total(disk, kind, now_ms, self.local)
                else {
                    continue;
                };
                attempted += 1;
                offered.insert((disk, kind));
                match (self.post)(sensor, gigabytes, comment.as_deref()) {
                    Ok(()) => {
                        sent += 1;
                        #[cfg(test)]
                        {
                            self.posts += 1;
                        }
                    }
                    Err(error) => failed.push(format!(
                        "disks: cannot post {} on {} disk ({disk}, {label}, {gigabytes} GB): \
                         {error}",
                        kind.sensor(),
                        node.name
                    )),
                }
            }
        }
        let marked = attempted == 0 || sent > 0;
        for line in failed {
            logger.error(if marked {
                format!("{line}; the day's other values went out, so it is not retried")
            } else {
                format!("{line}; every post failed, so the next sample in the window tries again")
            });
        }
        if marked {
            self.ledger.posted_day = Some(day);
            self.ledger_dirty = true;
            for disk in self.ledger.disks.keys() {
                let (sensors, amounts) =
                    written::describe_totals([Kind::Written, Kind::Read].map(|kind| {
                        let unoffered = !offered.contains(&(disk.as_str(), kind));
                        let total = self.ledger.day_total(disk, kind, now_ms, self.local);
                        (
                            kind,
                            total.filter(|_| unoffered).map(|(gigabytes, _)| gigabytes),
                        )
                    }));
                if !sensors.is_empty() {
                    let verb = if sensors.contains(" and ") {
                        "are"
                    } else {
                        "is"
                    };
                    logger.info(format!(
                        "disks: {disk}: its {sensors} for {label} ({amounts}) {verb} not posted: \
                         no filesystem on it is mounted with a registered sensor"
                    ));
                }
            }
        }
        (attempted, sent)
    }

    /// The physical disk behind `disk`: its WWID or serial from sysfs (cached while the name stays
    /// in `/proc/diskstats`; sysfs serves them from memory, the disk is not asked), else the mount
    /// points mounted on it now. An unmounted filesystem keeps its old disk name, so its mount
    /// point would tie a new disk under that name to the old one (#1489): it does not count.
    fn identity_of(&mut self, disk: &str, nodes: &[Node<'_>]) -> Option<String> {
        let sys_root = self.disks.sys_root.clone();
        let hardware = self
            .identities
            .entry(disk.to_string())
            .or_insert_with(|| hardware_identity(&sys_root, disk))
            .clone();
        hardware.or_else(|| {
            let mut points: Vec<String> = nodes
                .iter()
                .filter(|node| node.mounted && node.disk.as_deref() == Some(disk))
                .map(|node| node.fs.mount_point.to_string_lossy().into_owned())
                .collect();
            points.sort();
            (!points.is_empty()).then(|| format!("mounts:{}", points.join("|")))
        })
    }

    /// Add `disk`'s counters to its per-day totals. Each skip sets a new baseline for both
    /// counters, so it is logged once per event, in one line for both totals.
    fn sample_ledger(
        &mut self,
        disk: &str,
        sectors: diskstats::Sectors,
        now_ms: i64,
        nodes: &[Node<'_>],
        logger: &Logger,
    ) {
        // Today's volumes: every sample, whatever the rate makes of it (a long gap inside the
        // day still counts; the ledger has its own rules).
        let identity = self.identity_of(disk, nodes);
        match self.ledger.sample_counters(
            disk,
            identity.as_deref(),
            sectors,
            now_ms,
            self.local,
            DISK_SAMPLE_PERIOD,
        ) {
            Err(written::Skip::OtherDisk) => logger.info(format!(
                "disks: {disk} is not the disk that had this name before (or it cannot be \
                 told after a reboot); its written and read per-day totals start afresh"
            )),
            Err(written::Skip::ClockBackwards) => logger.info(format!(
                "disks: {disk}: the clock went backwards; the writes and reads since the \
                 previous sample are not counted in Written per day and Read per day"
            )),
            Err(written::Skip::GapAcrossDays) => logger.info(format!(
                "disks: {disk}: no sample across local midnight (the probe was not running); \
                 the writes and reads in that gap are not counted in Written per day and \
                 Read per day"
            )),
            _ => {}
        }
        self.ledger_dirty = true;
    }

    /// A day that missed its post window while the probe ran (a suspend over midnight,
    /// unreadable /proc/diskstats, every post in the window failed) is lost: say so, with its
    /// totals — one line per disk.
    fn log_missed_days(&mut self, logger: &Logger) {
        for lost in std::mem::take(&mut self.ledger.missed) {
            let (sensors, amounts) = lost.describe();
            logger.info(format!(
                "disks: {}: the day {} ended without its {sensors} post (the probe did not sample \
                 in its last 30 s, or every post there failed); its measured {amounts} are not \
                 posted",
                lost.disk,
                written::day_label(lost.day)
            ));
        }
    }

    /// The stop's last `/proc/diskstats` reading, for the per-day totals only: no speed and no
    /// post (the collector is stopping), so the writes and reads since the last sample — up to
    /// one sample period — still count when the host is shutting down (#1507).
    fn final_ledger_sample(&mut self, logger: &Logger) {
        let text = match std::fs::read_to_string(&self.diskstats) {
            Ok(text) => text,
            Err(error) => {
                logger.error(format!(
                    "disks: cannot read {} at stop: {error}; the writes and reads since the last \
                     sample are not counted in Written per day and Read per day",
                    self.diskstats.display()
                ));
                return;
            }
        };
        let now_ms = (self.clock)();
        let counters = diskstats::parse_diskstats(&text);
        let disks_handle = Arc::clone(&self.disks);
        let nodes = disks_handle.nodes.lock().unwrap_or_else(|p| p.into_inner());
        for disk in metered_disks(&nodes) {
            if let Some(&sectors) = counters.get(disk) {
                self.sample_ledger(disk, sectors, now_ms, &nodes, logger);
            }
        }
        // A stop just past midnight turns the day here, with no later sample to report it.
        self.log_missed_days(logger);
    }

    fn save_ledger(&mut self, logger: &Logger) {
        let Some(path) = &self.ledger_path else {
            return;
        };
        if !self.ledger_dirty {
            return;
        }
        self.ledger.prune((self.local)((self.clock)()).0);
        let what = "disks: written-per-day ledger";
        match self.ledger.save(path, &self.boot_id) {
            Ok(()) => {
                self.ledger_dirty = false;
                self.save_failures.succeeded(logger, what);
            }
            Err(error) => self.save_failures.failed(
                logger,
                what,
                &format!(
                    "cannot write {} ({error}); a restart would start the day's totals afresh",
                    path.display()
                ),
            ),
        }
    }
}

type PostFn = fn(&DoubleSensor<'_>, f64, Option<&str>) -> hsm_collector::Result<()>;

/// Hand one `Written per day` or `Read per day` value, with its comment if any, to the collector.
fn post_per_day(
    sensor: &DoubleSensor<'_>,
    gigabytes: f64,
    comment: Option<&str>,
) -> hsm_collector::Result<()> {
    match comment {
        Some(comment) => sensor.add_with(gigabytes, hsm_collector::SensorStatus::Ok, Some(comment)),
        None => sensor.add(gigabytes),
    }
}

/// The disks behind a mounted filesystem with a speed or per-day sensor.
fn metered_disks<'n>(nodes: &'n [Node<'_>]) -> BTreeSet<&'n str> {
    nodes
        .iter()
        .filter(|node| {
            node.mounted
                && (node.write_speed.is_some()
                    || node.read_speed.is_some()
                    || node.written_per_day.is_some()
                    || node.read_per_day.is_some())
        })
        .filter_map(|node| node.disk.as_deref())
        .collect()
}

/// `wwid:<…>` or `serial:<…>` of a whole disk from sysfs; `None` when the device exposes neither.
fn hardware_identity(sys_root: &Path, disk: &str) -> Option<String> {
    let block = sys_root.join("block").join(disk);
    [
        ("wwid", block.join("wwid")),
        ("wwid", block.join("device/wwid")),
        ("serial", block.join("device/serial")),
    ]
    .into_iter()
    .find_map(|(kind, path)| {
        let text = std::fs::read_to_string(path).ok()?;
        let value = text.trim();
        (!value.is_empty()).then(|| format!("{kind}:{value}"))
    })
}

fn unix_now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

impl Source for WriteSpeedSource<'_> {
    fn name(&self) -> &'static str {
        "disk write speed"
    }

    fn period(&self) -> Duration {
        DISK_SAMPLE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        // Days that ended while the probe was down are never posted: say so once, here only (the
        // day turn does not report them again) — one line per disk naming both totals.
        if std::mem::take(&mut self.unposted_pending) {
            let today = (self.local)((self.clock)()).0;
            for lost in self.ledger.unposted_days(today) {
                let (sensors, amounts) = lost.describe();
                logger.info(format!(
                    "disks: {}: the day {} ended while the probe was not running; its measured \
                     {amounts} are not posted as {sensors}",
                    lost.disk,
                    written::day_label(lost.day)
                ));
                self.ledger_dirty = true;
            }
        }
        let what = "disks: write speed";
        let text = match std::fs::read_to_string(&self.diskstats) {
            Ok(text) => text,
            Err(error) => {
                self.failures.failed(
                    logger,
                    what,
                    &format!("cannot read {}: {error}", self.diskstats.display()),
                );
                return;
            }
        };
        let now = Instant::now();
        let now_ms = (self.clock)();
        let counters = diskstats::parse_diskstats(&text);

        let disks_handle = Arc::clone(&self.disks);
        let nodes = disks_handle.nodes.lock().unwrap_or_else(|p| p.into_inner());
        let disks = metered_disks(&nodes);
        // A name absent from /proc/diskstats (unplugged, even while unmounted and not sampled) may
        // come back as another disk: forget what was cached or counted under it.
        self.identities
            .retain(|disk, _| counters.contains_key(disk.as_str()));
        self.ledger
            .forget_baselines_except(|disk| counters.contains_key(disk));
        let mut missing = Vec::new();
        for disk in disks {
            let Some(&sectors) = counters.get(disk) else {
                missing.push(disk.to_string());
                // Gone from the table: the next appearance starts a new baseline.
                self.rates.remove(disk);
                self.read_rates.remove(disk);
                self.ledger.forget_baseline(disk);
                self.identities.remove(disk);
                continue;
            };
            // Today's volumes: every sample, whatever the rate makes of it (a long gap inside the
            // day still counts; the ledger has its own rules).
            self.sample_ledger(disk, sectors, now_ms, &nodes, logger);
            let rate = self
                .rates
                .entry(disk.to_string())
                .or_insert_with(|| WriteRate::new(DISK_SAMPLE_PERIOD))
                .sample(sectors.written, now);
            // The read speed: the same sample's "sectors read". A baseline or an implausible
            // interval is the write rate's too (logged below).
            let read_rate = self
                .read_rates
                .entry(disk.to_string())
                .or_insert_with(|| WriteRate::new(DISK_SAMPLE_PERIOD))
                .sample(sectors.read, now);
            // One line per device reset (#1507): a real reset sets both counters back at once.
            match (
                matches!(rate, Err(Skip::CounterReset)),
                matches!(read_rate, Err(Skip::CounterReset)),
            ) {
                (true, true) => logger.info(format!(
                    "disks: the write and read counters of {disk} went backwards (device \
                     reset?); new baseline"
                )),
                (true, false) => logger.info(format!(
                    "disks: the write counter of {disk} went backwards (device reset?); new baseline"
                )),
                (false, true) => logger.info(format!(
                    "disks: the read counter of {disk} went backwards (device reset?); new baseline"
                )),
                (false, false) => {}
            }
            match rate {
                Ok(mb_per_second) => {
                    for node in nodes
                        .iter()
                        .filter(|node| node.mounted && node.disk.as_deref() == Some(disk))
                    {
                        if let Some(bar) = &node.write_speed {
                            if let Err(error) = bar.add(mb_per_second) {
                                logger.error(format!("disks: cannot post a write speed: {error}"));
                            }
                        }
                    }
                }
                Err(Skip::Baseline | Skip::CounterReset) => {}
                Err(Skip::AbnormalInterval) => logger.log(
                    Level::Debug,
                    &format!("disks: write-speed sample of {disk} skipped (interval out of range)"),
                ),
            }
            match read_rate {
                Ok(mb_per_second) => {
                    for node in nodes
                        .iter()
                        .filter(|node| node.mounted && node.disk.as_deref() == Some(disk))
                    {
                        if let Some(bar) = &node.read_speed {
                            if let Err(error) = bar.add(mb_per_second) {
                                logger.error(format!("disks: cannot post a read speed: {error}"));
                            }
                        }
                    }
                }
                Err(Skip::Baseline | Skip::CounterReset | Skip::AbnormalInterval) => {}
            }
        }
        if missing.is_empty() {
            self.failures.succeeded(logger, what);
        } else {
            self.failures.failed(
                logger,
                what,
                &format!("{} not in {}", missing.join(", "), self.diskstats.display()),
            );
        }
        self.log_missed_days(logger);
        // The day's only post, in its last six sample periods (30 s): one slow read or a late tick
        // still lands in it. What is written or read after the post counts towards the next day.
        // A window missed altogether (a suspend, a stopped probe) leaves the day unposted — logged.
        let (day, second) = (self.local)(now_ms);
        let period = i64::try_from(DISK_SAMPLE_PERIOD.as_secs()).unwrap_or(5);
        let day_ends = second + FINAL_READING_PERIODS * period >= 86_400
            && self.ledger.posted_day.is_none_or(|posted| posted < day);
        if day_ends {
            self.post_per_day_totals(&nodes, day, logger);
        }
        // The ledger is saved every 5 minutes (half a sample period of slack) and right after the
        // post, so a restart continues the day and never posts it twice. The fsync'd save runs
        // without the nodes lock: the space source shares it, and a slow state disk must not stall
        // its samples or re-scan.
        if day_ends || self.last_save.elapsed() + DISK_SAMPLE_PERIOD / 2 >= SPACE_PERIOD {
            self.last_save = Instant::now();
            drop(nodes);
            self.save_ledger(logger);
        }
    }

    fn stop(&mut self, logger: &Logger) {
        // A final reading first (#1507): after a reboot the saved counters are not trusted, so
        // without it what was written and read since the last sample would never be counted.
        self.final_ledger_sample(logger);
        // Today's totals and counters, so a restart continues the day.
        self.save_ledger(logger);
    }
}

#[cfg(test)]
pub mod tests {
    use super::*;

    /// The four garage-server filesystems after the naming rule, with their disks.
    pub const GARAGE_NODES: &[(&str, &str)] = &[
        ("root", "sdc"),
        ("wd4tb", "sda"),
        ("mediacentr", "sdb"),
        ("oldlinux", "sdb"),
    ];

    /// Every disk sensor path garage-server registers (at the product root, #1493).
    pub fn garage_paths() -> Vec<String> {
        GARAGE_NODES
            .iter()
            .flat_map(|(name, _)| {
                [
                    free_space_path(name),
                    free_space_percent_path(name),
                    free_inodes_percent_path(name),
                    write_speed_path(name),
                    read_speed_path(name),
                    written_per_day_path(name),
                    read_per_day_path(name),
                ]
            })
            .map(|path| path.to_string())
            .collect()
    }

    #[test]
    fn free_space_uses_the_fragment_size_and_whole_megabytes() {
        // garage-server's root filesystem on 2026-09-29.
        let stats = FsStats {
            fragment_size: 4096,
            blocks: 27_224_445,
            blocks_available: 10_056_585,
            files: 6_955_008,
            files_available: 5_765_593,
        };
        assert_eq!(stats.free_megabytes(), 39_283.0);
        assert_eq!(stats.free_space_percent(), Some(36.94));
        assert_eq!(stats.free_inodes_percent(), Some(82.9));
        let empty = FsStats {
            fragment_size: 4096,
            blocks: 0,
            blocks_available: 0,
            files: 0,
            files_available: 0,
        };
        assert_eq!(empty.free_space_percent(), None);
        assert_eq!(empty.free_inodes_percent(), None);
    }

    #[test]
    fn paths_follow_the_windows_per_drive_naming() {
        assert_eq!(
            free_space_path("wd4tb"),
            ".computer/Disks monitoring/Free space on wd4tb disk"
        );
        assert_eq!(
            write_speed_path("root"),
            ".computer/Disks monitoring/Average disk write speed on root disk"
        );
        assert_eq!(
            free_inodes_percent_path("root"),
            ".computer/Disks monitoring/Free inodes on root disk %"
        );
    }

    #[test]
    fn a_hung_statvfs_is_given_up_and_not_asked_again_while_it_hangs() {
        let flag = Arc::new(AtomicBool::new(false));
        let hung: StatvfsFn = |_| {
            std::thread::sleep(Duration::from_secs(2));
            Err(std::io::Error::from(std::io::ErrorKind::TimedOut))
        };
        let started = Instant::now();
        let error = statvfs_bounded(Path::new("/mnt/hung"), hung, &flag).unwrap_err();
        assert!(error.contains("did not return"), "{error}");
        assert!(started.elapsed() < Duration::from_secs(2));
        // The helper is still blocked: the next sample does not start a second one.
        let error = statvfs_bounded(Path::new("/mnt/hung"), hung, &flag).unwrap_err();
        assert!(error.contains("still not returned"), "{error}");
        // Once it returns, the filesystem is asked again.
        std::thread::sleep(Duration::from_millis(2200));
        let ok: StatvfsFn = |_| {
            Ok(FsStats {
                fragment_size: 4096,
                blocks: 1,
                blocks_available: 1,
                files: 1,
                files_available: 1,
            })
        };
        assert!(statvfs_bounded(Path::new("/mnt/hung"), ok, &flag).is_ok());
    }

    /// Paths `recording_statvfs` was asked for (the helper threads call a plain `fn`).
    static STATVFS_CALLS: Mutex<Vec<PathBuf>> = Mutex::new(Vec::new());

    fn recording_statvfs(path: &Path) -> std::io::Result<FsStats> {
        STATVFS_CALLS.lock().unwrap().push(path.to_path_buf());
        Ok(FsStats {
            fragment_size: 4096,
            blocks: 100,
            blocks_available: 50,
            files: 10,
            files_available: 5,
        })
    }

    #[cfg(unix)]
    #[test]
    fn every_sample_statvfses_only_what_is_mounted_and_resumes_a_replug_at_once() {
        use crate::logging::Level;
        use crate::probe_only::host::tests::FakeTree;
        use hsm_collector::CollectorOptions;

        let tree = FakeTree::new("per-sample");
        tree.file("mountinfo", mounts::tests::GARAGE_MOUNTINFO);
        let environment = HostEnvironment {
            sys_root: tree.0.join("sys"),
            mountinfo: tree.0.join("mountinfo"),
            fstab: tree.0.join("fstab"),
            diskstats: tree.0.join("diskstats"),
            statvfs: recording_statvfs,
            online_cpus: || Ok(1),
            docker_engine: |_| Box::new(crate::probe_only::docker::tests::FixtureEngine::garage()),
            docker_state: None,
            disk_names: None,
            disk_written: None,
            boot_id: PathBuf::from("/nonexistent/boot_id"),
        };
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        let collector = Collector::new(&options).expect("create");
        let logger = Logger::new(Level::Error, None);
        let (mut space, _) = build(&collector, &DisksConfig::default(), &environment, &logger);
        collector.start().expect("start");
        let asked = |sample: &mut SpaceSource<'_>| {
            STATVFS_CALLS.lock().unwrap().clear();
            // A recent re-scan: only the per-sample check runs.
            sample.last_scan = Instant::now();
            sample.sample(&logger);
            let mut calls = STATVFS_CALLS.lock().unwrap().clone();
            calls.sort();
            calls
        };

        // oldlinux unmounted between two re-scans: never statvfs'd (it would answer for `/`).
        let without_oldlinux = mounts::tests::GARAGE_MOUNTINFO
            .lines()
            .filter(|line| !line.contains("/mnt/oldlinux"))
            .collect::<Vec<_>>()
            .join("\n");
        tree.file("mountinfo", &without_oldlinux);
        let calls = asked(&mut space);
        assert!(
            !calls.contains(&PathBuf::from("/mnt/oldlinux")),
            "{calls:?}"
        );
        assert_eq!(calls.len(), 3);

        // Plugged back in (same device): resumed on this very sample, not at the next re-scan.
        tree.file("mountinfo", mounts::tests::GARAGE_MOUNTINFO);
        let calls = asked(&mut space);
        assert!(calls.contains(&PathBuf::from("/mnt/oldlinux")), "{calls:?}");
        assert_eq!(calls.len(), 4);
        collector.stop().expect("stop");
    }

    #[cfg(unix)]
    #[test]
    fn a_mount_that_appears_later_registers_at_runtime_and_one_that_goes_away_stops() {
        use crate::logging::Level;
        use crate::probe_only::host::tests::FakeTree;
        use hsm_collector::CollectorOptions;

        let tree = FakeTree::new("rescan");
        tree.file("mountinfo", mounts::tests::GARAGE_MOUNTINFO);
        tree.file("diskstats", diskstats::tests::GARAGE_DISKSTATS);
        diskstats::tests::garage_sysfs(&tree);
        let environment = HostEnvironment {
            sys_root: tree.0.join("sys"),
            mountinfo: tree.0.join("mountinfo"),
            fstab: tree.0.join("fstab"),
            diskstats: tree.0.join("diskstats"),
            statvfs: |_| {
                Ok(FsStats {
                    fragment_size: 4096,
                    blocks: 100,
                    blocks_available: 50,
                    files: 10,
                    files_available: 5,
                })
            },
            online_cpus: || Ok(1),
            docker_engine: |_| Box::new(crate::probe_only::docker::tests::FixtureEngine::garage()),
            docker_state: None,
            disk_names: None,
            disk_written: None,
            boot_id: PathBuf::from("/nonexistent/boot_id"),
        };
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        let collector = Collector::new(&options).expect("create");
        let logger = Logger::new(Level::Error, None);

        let (mut space, write) = build(&collector, &DisksConfig::default(), &environment, &logger);
        let mut write = write.expect("write speed on by default");
        collector.start().expect("start");
        // One /proc/diskstats read meters each of the three whole disks once.
        write.sample(&logger);
        let metered: Vec<&str> = write.rates.keys().map(String::as_str).collect();
        assert_eq!(metered, vec!["sda", "sdb", "sdc"]);
        let before = collector.registrations().len();
        assert_eq!(before, 28, "four filesystems x seven sensors");
        // One /proc/diskstats read feeds the read speed of the same three disks.
        let read: Vec<&str> = write.read_rates.keys().map(String::as_str).collect();
        assert_eq!(read, metered);

        // A USB stick is plugged in and the oldlinux partition is unmounted.
        let mounted = mounts::tests::GARAGE_MOUNTINFO
            .lines()
            .filter(|line| !line.contains("/mnt/oldlinux"))
            .chain(["900 676 8:49 / /media/usb rw,relatime - vfat /dev/sdd1 rw"])
            .collect::<Vec<_>>()
            .join("\n");
        tree.file("mountinfo", &mounted);

        // Force the 10-minute re-scan on the next sample.
        space.last_scan = Instant::now() - RESCAN_PERIOD;
        space.sample(&logger);

        let registrations = collector.registrations();
        let new_paths: Vec<&String> = registrations[before..].iter().collect();
        assert!(
            new_paths
                .iter()
                .any(|json| json.contains("Free space on usb disk %")),
            "{new_paths:#?}"
        );
        // No disk in sysfs for sdd: no write-speed sensor, the rest registers.
        assert!(new_paths.iter().all(
            |json| !json.contains("write speed on usb") && !json.contains("read speed on usb")
        ));
        assert_eq!(new_paths.len(), 3);
        let nodes = space.disks.nodes.lock().unwrap();
        let oldlinux = nodes.iter().find(|node| node.name == "oldlinux").unwrap();
        assert!(!oldlinux.mounted);
        drop(nodes);

        // Another stick in the same slot: same mount point, same name, same sensors — no second
        // registration of the `usb` paths.
        tree.file(
            "mountinfo",
            &mounted
                .replace("/dev/sdd1", "/dev/sde1")
                .replace("8:49", "8:65"),
        );
        let registered = collector.registrations().len();
        space.last_scan = Instant::now() - RESCAN_PERIOD;
        space.sample(&logger);
        assert_eq!(collector.registrations().len(), registered);
        let nodes = space.disks.nodes.lock().unwrap();
        let usb: Vec<_> = nodes.iter().filter(|node| node.name == "usb").collect();
        assert_eq!(usb.len(), 1);
        assert!(usb[0].mounted && usb[0].fs.source == "/dev/sde1");
        drop(nodes);
        collector.stop().expect("stop");
    }

    thread_local! {
        /// The wall clock of the written-today tests (each test runs on its own thread).
        static NOW_MS: std::cell::Cell<i64> = const { std::cell::Cell::new(0) };
    }

    fn test_clock() -> i64 {
        NOW_MS.with(std::cell::Cell::get)
    }

    #[cfg(unix)]
    #[test]
    fn written_per_day_follows_each_disks_counter_and_survives_a_restart() {
        use crate::logging::Level;
        use crate::probe_only::host::tests::FakeTree;
        use hsm_collector::CollectorOptions;

        let tree = FakeTree::new("written-today");
        tree.file("mountinfo", mounts::tests::GARAGE_MOUNTINFO);
        tree.file("diskstats", diskstats::tests::GARAGE_DISKSTATS);
        tree.file("boot_id", "4b1c7a52-0000-4000-8000-000000000001\n");
        diskstats::tests::garage_sysfs(&tree);
        let environment = HostEnvironment {
            sys_root: tree.0.join("sys"),
            mountinfo: tree.0.join("mountinfo"),
            fstab: tree.0.join("fstab"),
            diskstats: tree.0.join("diskstats"),
            // Not `recording_statvfs`: its call log belongs to the per-sample test.
            statvfs: |_| {
                Ok(FsStats {
                    fragment_size: 4096,
                    blocks: 100,
                    blocks_available: 50,
                    files: 10,
                    files_available: 5,
                })
            },
            online_cpus: || Ok(1),
            docker_engine: |_| Box::new(crate::probe_only::docker::tests::FixtureEngine::garage()),
            docker_state: None,
            disk_names: None,
            disk_written: Some(tree.0.join(written::FILE_NAME)),
            boot_id: tree.0.join("boot_id"),
        };
        // sdc wrote 2 000 000 sectors (1.024 GB) since the first read; the HDDs nothing.
        let later = diskstats::tests::GARAGE_DISKSTATS.replace(" 683671624 ", " 685671624 ");
        let start = written::tests::MIDNIGHT + 9 * 3_600_000;

        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        let logger = Logger::new(Level::Error, None);
        {
            let collector = Collector::new(&options).expect("create");
            let (_, write) = build(&collector, &DisksConfig::default(), &environment, &logger);
            let mut write = write.expect("write speed on by default");
            write.clock = test_clock;
            write.local = written::tests::utc;
            collector.start().expect("start");
            NOW_MS.with(|now| now.set(start));
            write.sample(&logger);
            tree.file("diskstats", &later);
            NOW_MS.with(|now| now.set(start + 5_000));
            // The 5-minute save is due: the ledger is written, but nothing is posted — the day's
            // only post comes at its end.
            write.last_save = Instant::now() - SPACE_PERIOD;
            write.sample(&logger);
            assert_eq!(write.posts, 0, "no post at a 5-minute tick");
            let at = start + 5_000;
            assert_eq!(
                write.ledger.today("sdc", at, written::tests::utc),
                Some((
                    1.024,
                    Some(
                        "measured since 09:00 local time (writes before that are not counted)"
                            .into()
                    )
                ))
            );
            assert_eq!(
                write
                    .ledger
                    .today("sda", at, written::tests::utc)
                    .unwrap()
                    .0,
                0.0
            );
            assert_eq!(
                write
                    .ledger
                    .today("sdb", at, written::tests::utc)
                    .unwrap()
                    .0,
                0.0
            );

            let registrations = collector.registrations();
            let root = registrations
                .iter()
                .find(|json| json.contains("Written per day on root disk"))
                .expect("registered")
                .clone();
            assert!(root.contains("\"OriginalUnit\":4"), "{root}");
            assert!(!root.contains("\"Statistics\":1"), "{root}");
            assert!(!root.contains("\"Alerts\":[{"), "{root}");
            // TTL 26 h (in .NET ticks): a day without its post shows as Timeout.
            assert!(root.contains("936000000000"), "{root}");
            // The accounting day runs from the previous day's post to this one (#1489).
            assert!(
                root.contains("from the previous day")
                    && root.contains("(about 23:59:30) to this one"),
                "{root}"
            );
            // mediacentr and oldlinux share sdb: each says so.
            assert!(registrations
                .iter()
                .any(|json| json.contains("Written per day on oldlinux disk")
                    && json.contains("also the volume written per day of: mediacentr")));
            collector.stop().expect("stop");
        }

        // Restarted an hour later in the same boot: sdc wrote 1.024 GB more meanwhile, and the day
        // continues from the ledger.
        tree.file(
            "diskstats",
            &diskstats::tests::GARAGE_DISKSTATS.replace(" 683671624 ", " 687671624 "),
        );
        let collector = Collector::new(&options).expect("create");
        let (_, write) = build(&collector, &DisksConfig::default(), &environment, &logger);
        let mut write = write.expect("write speed on by default");
        write.clock = test_clock;
        write.local = written::tests::utc;
        collector.start().expect("start");
        NOW_MS.with(|now| now.set(start + 3_600_000));
        write.sample(&logger);
        assert_eq!(
            write
                .ledger
                .today("sdc", start + 3_600_000, written::tests::utc)
                .unwrap()
                .0,
            2.048
        );
        // Through the day (09:00 → 23:59) no sample posts anything.
        for minutes in [5, 60, 600, 840] {
            NOW_MS.with(|now| now.set(start + minutes * 60_000));
            write.last_save = Instant::now() - SPACE_PERIOD;
            write.sample(&logger);
        }
        assert_eq!(write.posts, 0, "nothing before the day's end");
        // 23:59:33: the day's totals are posted, written and read once per filesystem (root,
        // wd4tb, mediacentr, oldlinux), and the day is remembered as posted.
        let late = written::tests::MIDNIGHT + 86_373_000;
        NOW_MS.with(|now| now.set(late));
        write.sample(&logger);
        assert_eq!(write.posts, 8);
        assert_eq!(write.ledger.posted_day, Some(written::tests::utc(late).0));
        NOW_MS.with(|now| now.set(late + 5_000));
        write.sample(&logger);
        assert_eq!(write.posts, 8, "exactly one post per sensor per day");
        {
            // A restart inside the window does not post the day again: the ledger remembers it.
            let again_collector = Collector::new(&options).expect("create");
            let (_, again) = build(
                &again_collector,
                &DisksConfig::default(),
                &environment,
                &logger,
            );
            let mut again = again.expect("write speed on by default");
            again.clock = test_clock;
            again.local = written::tests::utc;
            again_collector.start().expect("start");
            NOW_MS.with(|now| now.set(late + 10_000));
            again.sample(&logger);
            assert_eq!(again.posts, 0, "a restart does not post the day twice");
            again_collector.stop().expect("stop");
        }

        // sdc leaves /proc/diskstats (unplugged): its cached identity and its counter go, so a
        // different disk that later gets the name cannot inherit either.
        assert!(write.identities.contains_key("sdc"));
        tree.file(
            "diskstats",
            &diskstats::tests::GARAGE_DISKSTATS
                .lines()
                .filter(|line| !line.contains(" sdc "))
                .collect::<Vec<_>>()
                .join("\n"),
        );
        write.sample(&logger);
        assert!(!write.identities.contains_key("sdc"));
        assert_eq!(write.ledger.disks["sdc"].baseline, None);
        collector.stop().expect("stop");
    }

    const HOUR_MS: i64 = 3_600_000;
    const DAY_MS: i64 = 24 * HOUR_MS;

    /// garage-server's mounts, disks and sysfs, with a boot id and a ledger file.
    #[cfg(unix)]
    fn written_host(tag: &str) -> (crate::probe_only::host::tests::FakeTree, HostEnvironment) {
        let tree = crate::probe_only::host::tests::FakeTree::new(tag);
        tree.file("mountinfo", mounts::tests::GARAGE_MOUNTINFO);
        tree.file("diskstats", diskstats::tests::GARAGE_DISKSTATS);
        tree.file("boot_id", "4b1c7a52-0000-4000-8000-000000000001\n");
        diskstats::tests::garage_sysfs(&tree);
        let environment = HostEnvironment {
            sys_root: tree.0.join("sys"),
            mountinfo: tree.0.join("mountinfo"),
            fstab: tree.0.join("fstab"),
            diskstats: tree.0.join("diskstats"),
            statvfs: |_| {
                Ok(FsStats {
                    fragment_size: 4096,
                    blocks: 100,
                    blocks_available: 50,
                    files: 10,
                    files_available: 5,
                })
            },
            online_cpus: || Ok(1),
            docker_engine: |_| Box::new(crate::probe_only::docker::tests::FixtureEngine::garage()),
            docker_state: None,
            disk_names: None,
            disk_written: Some(tree.0.join(written::FILE_NAME)),
            boot_id: tree.0.join("boot_id"),
        };
        (tree, environment)
    }

    /// sdc (the root disk) wrote 2 000 000 sectors (1.024 GB) since `GARAGE_DISKSTATS`.
    fn diskstats_later() -> String {
        diskstats::tests::GARAGE_DISKSTATS.replace(" 683671624 ", " 685671624 ")
    }

    fn test_collector() -> Collector {
        let mut options =
            hsm_collector::CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        Collector::new(&options).expect("create")
    }

    /// The write source on the test clock and a UTC "local" calendar.
    fn written_source<'c>(
        collector: &'c Collector,
        environment: &HostEnvironment,
        logger: &Logger,
    ) -> WriteSpeedSource<'c> {
        let (_, write) = build(collector, &DisksConfig::default(), environment, logger);
        let mut write = write.expect("write speed on by default");
        write.clock = test_clock;
        write.local = written::tests::utc;
        write
    }

    fn capturing_logger() -> (Logger, Arc<Mutex<Vec<String>>>) {
        let lines = Arc::new(Mutex::new(Vec::new()));
        let sink = Arc::clone(&lines);
        let logger = Logger::with_sink(Level::Debug, move |line: &str| {
            sink.lock().unwrap().push(line.to_string())
        });
        (logger, lines)
    }

    fn at(unix_ms: i64) {
        NOW_MS.with(|now| now.set(unix_ms));
    }

    #[cfg(unix)]
    #[test]
    fn a_day_that_ended_while_the_probe_was_down_is_reported_once() {
        let (tree, environment) = written_host("written-down");
        let quiet = Logger::new(Level::Error, None);
        let day = written::tests::MIDNIGHT + 9 * HOUR_MS;
        {
            // Day D, 09:00: the probe measures the day, then stops before midnight.
            let collector = test_collector();
            let mut write = written_source(&collector, &environment, &quiet);
            collector.start().expect("start");
            at(day);
            write.sample(&quiet);
            tree.file("diskstats", &diskstats_later());
            at(day + 5_000);
            write.sample(&quiet);
            write.stop(&quiet);
            collector.stop().expect("stop");
        }

        // Started again on D+1, same boot: the day is reported once, as ended while the probe was
        // not running — not a second time, with another reason, when the first sample turns it.
        let (logger, lines) = capturing_logger();
        let collector = test_collector();
        let mut write = written_source(&collector, &environment, &logger);
        collector.start().expect("start");
        at(day + DAY_MS);
        write.sample(&logger);
        at(day + DAY_MS + 5_000);
        write.sample(&logger);
        collector.stop().expect("stop");
        let label = written::day_label(written::tests::utc(day).0);
        let lines = lines.lock().unwrap();
        for disk in ["sda", "sdb", "sdc"] {
            let about: Vec<&String> = lines
                .iter()
                .filter(|line| line.contains(&format!("{disk}: the day {label} ended")))
                .collect();
            assert_eq!(about.len(), 1, "{disk}: {lines:#?}");
            assert!(
                about[0].contains("ended while the probe was not running"),
                "{about:#?}"
            );
        }
        assert!(
            lines.iter().any(|line| line.contains("sdc")
                && line.contains("the day")
                && line.contains(
                    "1.024 GB written and 0 GB read are not posted as Written per \
                     day and Read per day"
                )),
            "both totals are named, in the one line: {lines:#?}"
        );
    }

    /// sdc wrote AND read 2 000 000 sectors (1.024 GB each) since `GARAGE_DISKSTATS`.
    fn diskstats_written_and_read_later() -> String {
        diskstats_later().replace(" 161806974 ", " 163806974 ")
    }

    #[cfg(unix)]
    #[test]
    fn the_stop_takes_a_final_reading_so_a_reboot_keeps_the_last_seconds() {
        let (tree, environment) = written_host("written-stop-final");
        let quiet = Logger::new(Level::Error, None);
        let start = written::tests::MIDNIGHT + 9 * HOUR_MS;
        {
            let collector = test_collector();
            let mut write = written_source(&collector, &environment, &quiet);
            collector.start().expect("start");
            at(start);
            write.sample(&quiet);
            // Written and read after the last sample, then the probe stops for a shutdown: the
            // stop's own reading counts them (#1507).
            tree.file("diskstats", &diskstats_written_and_read_later());
            at(start + 3_000);
            write.stop(&quiet);
            collector.stop().expect("stop");
        }

        // The host rebooted: the saved counters are not trusted, the day's totals continue.
        tree.file("boot_id", "4b1c7a52-0000-4000-8000-000000000002\n");
        tree.file("diskstats", diskstats::tests::GARAGE_DISKSTATS);
        let collector = test_collector();
        let mut write = written_source(&collector, &environment, &quiet);
        collector.start().expect("start");
        let after = start + 10 * 60_000;
        at(after);
        write.sample(&quiet);
        collector.stop().expect("stop");
        for kind in [Kind::Written, Kind::Read] {
            assert_eq!(
                write
                    .ledger
                    .day_total("sdc", kind, after, written::tests::utc)
                    .map(|(gigabytes, _)| gigabytes),
                Some(1.024),
                "{kind:?}"
            );
        }
    }

    #[cfg(unix)]
    #[test]
    fn one_device_reset_is_one_log_line() {
        let (tree, environment) = written_host("written-reset-line");
        let (logger, lines) = capturing_logger();
        let collector = test_collector();
        let mut write = written_source(&collector, &environment, &logger);
        collector.start().expect("start");
        let start = written::tests::MIDNIGHT + 9 * HOUR_MS;
        at(start);
        write.sample(&logger);
        // A device reset sets both of sdc's counters back.
        tree.file(
            "diskstats",
            &diskstats::tests::GARAGE_DISKSTATS
                .replace(" 683671624 ", " 1000 ")
                .replace(" 161806974 ", " 1000 "),
        );
        at(start + 5_000);
        write.sample(&logger);
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        let resets: Vec<&String> = lines
            .iter()
            .filter(|line| line.contains("went backwards"))
            .collect();
        assert_eq!(resets.len(), 1, "{lines:#?}");
        assert!(
            resets[0].contains("the write and read counters of sdc went backwards"),
            "{resets:#?}"
        );
    }

    thread_local! {
        /// How many of the next `Written per day` posts [`failing_post`] fails.
        static FAIL_POSTS: std::cell::Cell<usize> = const { std::cell::Cell::new(0) };
    }

    fn failing_post(
        sensor: &DoubleSensor<'_>,
        gigabytes: f64,
        comment: Option<&str>,
    ) -> hsm_collector::Result<()> {
        let fail = FAIL_POSTS.with(|left| {
            let n = left.get();
            left.set(n.saturating_sub(1));
            n > 0
        });
        if fail {
            return Err(hsm_collector::Error::Abi {
                operation: "add double value",
                code: -1,
                message: "injected".into(),
            });
        }
        post_per_day(sensor, gigabytes, comment)
    }

    #[cfg(unix)]
    #[test]
    fn a_failed_post_is_retried_in_the_window_and_what_is_lost_is_named() {
        let (tree, environment) = written_host("written-retry");
        let (logger, lines) = capturing_logger();
        let collector = test_collector();
        let mut write = written_source(&collector, &environment, &logger);
        write.post = failing_post;
        collector.start().expect("start");
        let midnight = written::tests::MIDNIGHT;
        let window = 86_373_000; // 23:59:33
        let label = |day_start: i64| written::day_label(written::tests::utc(day_start).0);
        let disks = Arc::clone(&write.disks);

        // Day D, nothing measured (attempted == 0): nothing to post, and the day is marked posted.
        at(midnight + window);
        {
            let nodes = disks.nodes.lock().unwrap();
            let day = written::tests::utc(midnight).0;
            assert_eq!(write.post_per_day_totals(&nodes, day, &logger), (0, 0));
            assert_eq!(write.ledger.posted_day, Some(day));
        }
        assert_eq!(write.posts, 0);

        // Day D+1: measured from 09:00. Every post of the window's first sample fails
        // (attempted > 0, sent == 0): the day is not marked, and the next sample posts it.
        let d1 = midnight + DAY_MS;
        at(d1 + 9 * HOUR_MS);
        write.sample(&logger);
        tree.file("diskstats", &diskstats_later());
        at(d1 + 9 * HOUR_MS + 5_000);
        write.sample(&logger);
        at(d1 + window);
        // Four filesystems, a written and a read total each.
        FAIL_POSTS.with(|left| left.set(8));
        {
            let nodes = disks.nodes.lock().unwrap();
            let day = written::tests::utc(d1).0;
            assert_eq!(write.post_per_day_totals(&nodes, day, &logger), (8, 0));
            assert_eq!(write.ledger.posted_day, Some(day - 1), "not marked");
        }
        {
            let lines = lines.lock().unwrap();
            let retried: Vec<&String> = lines
                .iter()
                .filter(|line| line.contains("every post failed, so the next sample"))
                .collect();
            assert_eq!(retried.len(), 8, "{lines:#?}");
            let d1_label = label(d1);
            assert!(
                retried.iter().any(|line| line.contains(&format!(
                    "Written per day on root disk (sdc, {d1_label}, 1.024 GB)"
                ))),
                "{retried:#?}"
            );
            assert!(
                retried.iter().any(|line| line.contains(&format!(
                    "Read per day on root disk (sdc, {d1_label}, 0 GB)"
                ))),
                "{retried:#?}"
            );
        }
        at(d1 + window + 5_000);
        write.sample(&logger);
        assert_eq!(
            write.posts, 8,
            "the next sample in the window posts the day"
        );
        assert_eq!(write.ledger.posted_day, Some(written::tests::utc(d1).0));

        // Day D+2: wd4tb (sda) is unmounted after its day was measured, and one of the other
        // six posts fails. The day is marked (values went out); what it loses is named.
        let d2 = midnight + 2 * DAY_MS;
        at(d2 + 9 * HOUR_MS);
        write.sample(&logger);
        at(d2 + 9 * HOUR_MS + 5_000);
        write.sample(&logger);
        disks
            .nodes
            .lock()
            .unwrap()
            .iter_mut()
            .filter(|node| node.name == "wd4tb")
            .for_each(|node| node.mounted = false);
        FAIL_POSTS.with(|left| left.set(1));
        at(d2 + window);
        write.sample(&logger);
        assert_eq!(
            write.posts, 13,
            "five of the six values of the three mounted filesystems posted"
        );
        assert_eq!(write.ledger.posted_day, Some(written::tests::utc(d2).0));
        let d2_label = label(d2);
        let lines = lines.lock().unwrap();
        let lost: Vec<&String> = lines
            .iter()
            .filter(|line| line.contains("so it is not retried"))
            .collect();
        assert_eq!(lost.len(), 1, "{lines:#?}");
        assert!(lost[0].contains(&format!("{d2_label}, 0 GB)")), "{lost:#?}");
        // One line for the disk, naming both totals.
        let unmounted: Vec<&String> = lines
            .iter()
            .filter(|line| line.contains("sda: its "))
            .collect();
        assert_eq!(unmounted.len(), 1, "{lines:#?}");
        assert!(
            unmounted[0].contains(&format!(
                "sda: its Written per day and Read per day for {d2_label} (0 GB written and 0 GB \
                 read) are not posted"
            )),
            "{unmounted:#?}"
        );
        drop(lines);
        collector.stop().expect("stop");
    }

    thread_local! {
        /// What [`recording_post`] handed to the collector: `(GB, comment)`.
        static POSTED: std::cell::RefCell<Vec<(f64, Option<String>)>> =
            const { std::cell::RefCell::new(Vec::new()) };
    }

    fn recording_post(
        sensor: &DoubleSensor<'_>,
        gigabytes: f64,
        comment: Option<&str>,
    ) -> hsm_collector::Result<()> {
        POSTED.with(|posted| {
            posted
                .borrow_mut()
                .push((gigabytes, comment.map(str::to_string)))
        });
        post_per_day(sensor, gigabytes, comment)
    }

    #[cfg(unix)]
    #[test]
    fn read_per_day_is_registered_and_posted_with_written_per_day() {
        let (tree, environment) = written_host("read-per-day");
        let logger = Logger::new(Level::Error, None);
        let collector = test_collector();
        let mut write = written_source(&collector, &environment, &logger);
        write.post = recording_post;
        collector.start().expect("start");

        let registrations = collector.registrations();
        let root = registrations
            .iter()
            .find(|json| json.contains("Read per day on root disk"))
            .expect("registered")
            .clone();
        // Written per day's shape: decimal GB, no statistics, no alert, TTL 26 h.
        assert!(root.contains("\"OriginalUnit\":4"), "{root}");
        assert!(!root.contains("\"Statistics\":1"), "{root}");
        assert!(!root.contains("\"Alerts\":[{"), "{root}");
        assert!(root.contains("936000000000"), "{root}");
        assert!(
            root.contains("/proc/diskstats sectors read")
                && root.contains("(about 23:59:30) to this one"),
            "{root}"
        );
        assert!(registrations
            .iter()
            .any(|json| json.contains("Read per day on oldlinux disk")
                && json.contains("also the volume read per day of: mediacentr")));
        // Every filesystem with Written per day has Read per day.
        for (name, _) in GARAGE_NODES {
            for path in [written_per_day_path(name), read_per_day_path(name)] {
                assert!(
                    registrations.iter().any(|json| json.contains(&path)),
                    "{path}"
                );
            }
        }

        // Day D from 09:00: sdc writes 1.024 GB and reads 2.048 GB.
        let day = written::tests::MIDNIGHT + 9 * HOUR_MS;
        at(day);
        write.sample(&logger);
        tree.file(
            "diskstats",
            &diskstats_later().replace(" 161806974 ", " 165806974 "),
        );
        at(day + 5_000);
        write.sample(&logger);
        let window = written::tests::MIDNIGHT + 86_373_000; // 23:59:33
        at(window);
        write.sample(&logger);
        assert_eq!(write.posts, 8, "four filesystems, written and read");
        let posted = POSTED.with(|posted| posted.take());
        let since = |what: &str| {
            Some(format!(
                "measured since 09:00 local time ({what} before that are not counted)"
            ))
        };
        assert!(posted.contains(&(1.024, since("writes"))), "{posted:?}");
        assert!(posted.contains(&(2.048, since("reads"))), "{posted:?}");
        assert_eq!(
            posted
                .iter()
                .filter(|(_, comment)| *comment == since("reads"))
                .count(),
            4,
            "{posted:?}"
        );

        // Day D+1 was watched from its start (sampled through midnight after D's post): no
        // comment. sdc reads another 1.024 GB during it.
        for after_post in (5_000..=40_000).step_by(5_000) {
            at(window + after_post);
            write.sample(&logger);
        }
        tree.file(
            "diskstats",
            &diskstats_later().replace(" 161806974 ", " 167806974 "),
        );
        at(window + 9 * HOUR_MS);
        write.sample(&logger);
        at(window + DAY_MS);
        write.sample(&logger);
        assert_eq!(write.posts, 16);
        let posted = POSTED.with(|posted| posted.take());
        assert!(posted.contains(&(1.024, None)), "{posted:?}");
        assert!(
            posted.iter().all(|(_, comment)| comment.is_none()),
            "{posted:?}"
        );
        collector.stop().expect("stop");
    }

    /// A registration without its `Description` (the one field the read speed words differently).
    fn without_description(json: &str) -> String {
        let start = json.find("\"Description\":\"").expect("a description");
        let end = start + json[start..].find("\",\"EnumOptions\"").expect("its end");
        format!("{}{}", &json[..start], &json[end + 2..])
    }

    #[cfg(unix)]
    #[test]
    fn read_speed_mirrors_the_write_speed_registration() {
        let (_tree, environment) = written_host("read-speed");
        let logger = Logger::new(Level::Error, None);
        let collector = test_collector();
        let _write = written_source(&collector, &environment, &logger);
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        let find = |path: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("\"Path\":\"{path}\"")))
                .unwrap_or_else(|| panic!("{path} registered"))
                .clone()
        };
        for (name, disk) in GARAGE_NODES {
            let write = find(&write_speed_path(name));
            let read = find(&read_speed_path(name));
            // The same bar, unit, statistics, TTL and (no) alerts: only the path and the words.
            assert_eq!(
                without_description(&read),
                without_description(&write).replace("disk write speed", "disk read speed")
            );
            assert!(
                read.contains(&format!(
                    "Average read speed of the whole disk {disk} under"
                )) && read.contains("/proc/diskstats sectors read"),
                "{read}"
            );
        }
    }

    #[cfg(unix)]
    #[test]
    fn an_unmounted_filesystem_does_not_count_towards_a_disks_mount_point_identity() {
        let tree = crate::probe_only::host::tests::FakeTree::new("identity-mounted");
        tree.file("mountinfo", "");
        let environment = HostEnvironment {
            sys_root: tree.0.join("sys"),
            mountinfo: tree.0.join("mountinfo"),
            fstab: tree.0.join("fstab"),
            diskstats: tree.0.join("diskstats"),
            statvfs,
            online_cpus: || Ok(1),
            docker_engine: |_| Box::new(crate::probe_only::docker::tests::FixtureEngine::garage()),
            docker_state: None,
            disk_names: None,
            disk_written: None,
            boot_id: PathBuf::from("/nonexistent/boot_id"),
        };
        let collector = test_collector();
        let logger = Logger::new(Level::Error, None);
        let mut write = written_source(&collector, &environment, &logger);
        // No WWID and no serial in sysfs: the identity falls back to the mount points.
        let node = |mount_point: &str, mounted: bool| Node {
            name: "usb".into(),
            fs: Filesystem {
                key: "/dev/sdd1".into(),
                mount_point: PathBuf::from(mount_point),
                mount_points: vec![PathBuf::from(mount_point)],
                fs_type: "vfat".into(),
                source: "/dev/sdd1".into(),
                device: "8:49".into(),
            },
            disk: Some("sdd".into()),
            mounted,
            free_mb: None,
            free_percent: None,
            free_inodes: None,
            inodes_pending: false,
            write_speed: None,
            read_speed: None,
            written_per_day: None,
            read_per_day: None,
            in_flight: Arc::new(AtomicBool::new(false)),
            failures: FailureLog::default(),
        };
        // USB disk A was mounted at /mnt/usb as sdd and unplugged; disk B arrived as sdd and is
        // mounted at /media/b. A's node keeps the name sdd, but it is not mounted: it does not
        // count, so B does not share A's mount point and does not continue A's day.
        let nodes = [node("/mnt/usb", false), node("/media/b", true)];
        let identity = write.identity_of("sdd", &nodes);
        assert_eq!(identity.as_deref(), Some("mounts:/media/b"));
        assert_eq!(
            written::same_disk("mounts:/mnt/usb", identity.as_deref().unwrap()),
            Some(false)
        );
        // Every mounted filesystem on the disk does count.
        let nodes = [node("/mnt/usb", true), node("/media/b", true)];
        assert_eq!(
            write.identity_of("sdd", &nodes).as_deref(),
            Some("mounts:/media/b|/mnt/usb")
        );
    }

    #[test]
    fn names_survive_a_restart_with_a_different_set_of_mounts() {
        use crate::logging::Level;
        use crate::probe_only::host::tests::FakeTree;
        use hsm_collector::CollectorOptions;

        let tree = FakeTree::new("names-restart");
        let names_path = tree.0.join(names::FILE_NAME);
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        let collector = Collector::new(&options).expect("create");
        let logger = Logger::new(Level::Error, None);
        let disks = |mountinfo: &str| {
            tree.file("mountinfo", mountinfo);
            Disks {
                collector: &collector,
                config: DisksConfig::default(),
                sys_root: tree.0.join("sys"),
                mountinfo: tree.0.join("mountinfo"),
                fstab: tree.0.join("fstab"),
                statvfs,
                names: Mutex::new(names::load(&names_path, &logger)),
                names_path: Some(names_path.clone()),
                hidden_reported: Mutex::new(BTreeSet::new()),
                nodes: Mutex::new(Vec::new()),
            }
        };
        let named = |disks: &Disks<'_>| -> BTreeMap<String, String> {
            disks
                .scan(&logger)
                .expect("scan")
                .into_iter()
                .map(|(fs, name)| (fs.mount_point.to_string_lossy().into_owned(), name))
                .collect()
        };

        // First run: only /srv/data, so it is `data`.
        let first = named(&disks("1 0 8:2 / /srv/data rw - ext4 /dev/sda2 rw\n"));
        assert_eq!(first["/srv/data"], "data");

        // After a restart /mnt/data is mounted too. Without the persisted names both would fall
        // back to their paths and /srv/data would move to `_srv_data`, splitting its history.
        let second = named(&disks(
            "1 0 8:2 / /srv/data rw - ext4 /dev/sda2 rw\n2 0 8:3 / /mnt/data rw - ext4 /dev/sda3 rw\n",
        ));
        assert_eq!(second["/srv/data"], "data");
        assert_eq!(second["/mnt/data"], "_mnt_data");
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn statvfs_reads_the_root_filesystem() {
        let stats = statvfs(Path::new("/")).expect("statvfs /");
        assert!(stats.blocks > 0 && stats.fragment_size > 0);
        assert!(stats.blocks_available <= stats.blocks);
    }
}
