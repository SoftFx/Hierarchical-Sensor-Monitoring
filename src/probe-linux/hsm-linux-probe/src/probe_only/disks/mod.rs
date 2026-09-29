//! Disk sensors for every mounted real filesystem (#1481): free space (MB and %), free inodes (%)
//! and the write speed of the disk underneath.
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
//!
//! Every sensor carries a 15-minute TTL (three periods of the 5-minute ones): a filesystem that
//! stops being sampled — unmounted, or its `statvfs` hanging — turns to Timeout on the server.
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

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use hsm_collector::{
    instant_hourly_schedule_anchor, Alert, AlertCombination, AlertDestination, AlertIcon,
    AlertKind, AlertOperation, AlertProperty, AlertRepeat, AlertTarget, Collector, DoubleBarSensor,
    DoubleSensor, SensorOptions, STATISTICS_EMA,
};

use self::diskstats::{Skip, WriteRate};
use self::mounts::Filesystem;
use super::{FailureLog, HostEnvironment, Source};
use crate::config::DisksConfig;
use crate::logging::{Level, Logger};

/// Free space / inodes posting period.
pub const SPACE_PERIOD: Duration = Duration::from_secs(300);
/// How often the mount table is re-read for filesystems that appeared or went away.
pub const RESCAN_PERIOD: Duration = Duration::from_secs(600);
/// Write-speed sampling period (the managed `BarTickPeriod`); the bar is 5 minutes.
pub const WRITE_SAMPLE_PERIOD: Duration = Duration::from_secs(5);
const WRITE_BAR_PERIOD: Duration = Duration::from_secs(300);
/// Carried by the ABI for parity with the managed `PostDataPeriod`.
const WRITE_BAR_POST_PERIOD: Duration = Duration::from_secs(15);
const WRITE_BAR_PRECISION: i32 = 2;
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
    statvfs: StatvfsFn,
    /// Mount point → name for every filesystem ever named. Names are never reused or changed, and
    /// the map is persisted in `names_path`, so they survive restarts too.
    names: Mutex<BTreeMap<String, String>>,
    names_path: Option<PathBuf>,
    nodes: Mutex<Vec<Node<'c>>>,
}

impl<'c> Disks<'c> {
    /// Read the mount table and name what is new (persisting new names). `Err` when the table
    /// cannot be read.
    fn scan(&self, logger: &Logger) -> Result<Vec<(Filesystem, String)>, String> {
        let text = std::fs::read_to_string(&self.mountinfo)
            .map_err(|error| format!("cannot read {}: {error}", self.mountinfo.display()))?;
        let filesystems =
            mounts::real_filesystems(&mounts::parse_mountinfo(&text), &self.config.exclude);
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
        let known: BTreeSet<String> = {
            let nodes = self.nodes.lock().unwrap_or_else(|p| p.into_inner());
            nodes.iter().map(|node| node.fs.key.clone()).collect()
        };
        // Discovery touches the filesystem: done before taking the nodes lock.
        let found: Vec<Discovered> = current
            .iter()
            .filter(|(fs, _)| !known.contains(&fs.key))
            .map(|(fs, name)| self.discover(fs.clone(), name.clone()))
            .collect();

        let mut nodes = self.nodes.lock().unwrap_or_else(|p| p.into_inner());
        let present: BTreeMap<&str, &Filesystem> = current
            .iter()
            .map(|(fs, _)| (fs.key.as_str(), fs))
            .collect();
        for node in nodes.iter_mut() {
            match present.get(node.fs.key.as_str()) {
                Some(fs) => {
                    if !node.mounted {
                        logger.info(format!(
                            "disks: '{}' is mounted again at {}",
                            node.name,
                            fs.mount_point.display()
                        ));
                    }
                    node.mounted = true;
                    node.fs = (*fs).clone();
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
    let write_speed = disk.as_ref().and_then(|disk| {
        let sharing = by_disk
            .get(disk)
            .map(|names| {
                names
                    .iter()
                    .filter(|other| **other != name)
                    .cloned()
                    .collect::<Vec<_>>()
            })
            .unwrap_or_default();
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
                "Average write speed of the whole disk {disk} under {where_}, in MB/s: \
                 /proc/diskstats sectors written, one sample every 5 s into a 5-minute bar.{shared}"
            ));
        match collector.double_bar_sensor(
            &write_speed_path(&name),
            WRITE_BAR_PERIOD,
            WRITE_BAR_POST_PERIOD,
            WRITE_BAR_PRECISION,
            &options,
        ) {
            Ok(sensor) => Some(sensor),
            Err(error) => {
                logger.error(format!(
                    "cannot register {}: {error}",
                    write_speed_path(&name)
                ));
                None
            }
        }
    });
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
        statvfs: environment.statvfs,
        names: Mutex::new(
            environment
                .disk_names
                .as_deref()
                .map(|path| names::load(path, logger))
                .unwrap_or_default(),
        ),
        names_path: environment.disk_names.clone(),
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
    };
    let write = if config.write_speed {
        Some(WriteSpeedSource {
            disks,
            diskstats: environment.diskstats.clone(),
            rates: BTreeMap::new(),
            failures: FailureLog::default(),
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

        // statvfs runs without the nodes lock held, so the write-speed thread is never held up.
        let targets: Vec<(usize, PathBuf, Arc<AtomicBool>)> = {
            let nodes = self.disks.nodes.lock().unwrap_or_else(|p| p.into_inner());
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
                        // The discovery statvfs failed; this first answer decides.
                        node.inodes_pending = false;
                        if stats.files > 0 {
                            node.free_inodes =
                                register_inodes(self.disks.collector, logger, &node.name, &node.fs);
                        } else {
                            log_no_inodes(logger, &node.name, &node.fs);
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
/// bar of every mounted filesystem on that disk.
struct WriteSpeedSource<'c> {
    disks: Arc<Disks<'c>>,
    diskstats: PathBuf,
    rates: BTreeMap<String, WriteRate>,
    failures: FailureLog,
}

impl Source for WriteSpeedSource<'_> {
    fn name(&self) -> &'static str {
        "disk write speed"
    }

    fn period(&self) -> Duration {
        WRITE_SAMPLE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
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
        let counters = diskstats::parse_diskstats(&text);

        let nodes = self.disks.nodes.lock().unwrap_or_else(|p| p.into_inner());
        let disks: BTreeSet<&str> = nodes
            .iter()
            .filter(|node| node.mounted && node.write_speed.is_some())
            .filter_map(|node| node.disk.as_deref())
            .collect();
        let mut missing = Vec::new();
        for disk in disks {
            let Some(&written) = counters.get(disk) else {
                missing.push(disk.to_string());
                // Gone from the table: the next appearance starts a new baseline.
                self.rates.remove(disk);
                continue;
            };
            let rate = self
                .rates
                .entry(disk.to_string())
                .or_insert_with(|| WriteRate::new(WRITE_SAMPLE_PERIOD))
                .sample(written, now);
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
                Err(Skip::Baseline) => {}
                Err(Skip::CounterReset) => logger.info(format!(
                    "disks: the write counter of {disk} went backwards (device reset?); new baseline"
                )),
                Err(Skip::AbnormalInterval) => logger.log(Level::Debug, &format!(
                    "disks: write-speed sample of {disk} skipped (interval out of range)"
                )),
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

    /// Every disk sensor path garage-server registers.
    pub fn garage_paths(computer: &str) -> Vec<String> {
        GARAGE_NODES
            .iter()
            .flat_map(|(name, _)| {
                [
                    free_space_path(name),
                    free_space_percent_path(name),
                    free_inodes_percent_path(name),
                    write_speed_path(name),
                ]
            })
            .map(|path| format!("{computer}/{path}"))
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
        };
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        options.computer_name = Some("garage-server".into());
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
        assert_eq!(before, 16, "four filesystems x four sensors");

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
        assert!(new_paths
            .iter()
            .all(|json| !json.contains("write speed on usb")));
        assert_eq!(new_paths.len(), 3);
        let nodes = space.disks.nodes.lock().unwrap();
        let oldlinux = nodes.iter().find(|node| node.name == "oldlinux").unwrap();
        assert!(!oldlinux.mounted);
        drop(nodes);
        collector.stop().expect("stop");
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
                statvfs,
                names: Mutex::new(names::load(&names_path, &logger)),
                names_path: Some(names_path.clone()),
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
