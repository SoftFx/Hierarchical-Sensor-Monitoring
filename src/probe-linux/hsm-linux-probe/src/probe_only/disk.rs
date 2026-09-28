//! Disk sensors: `.computer/Disks monitoring/Free space on disk %` and `… /Free inodes %`.
//!
//! # Which filesystem
//!
//! The one that actually holds `/srv/docker` (the Docker data root), resolved through
//! `/proc/self/mountinfo` rather than assumed to be `/`: the mount whose mount point is the longest
//! path-prefix of the canonical target (the last such entry when mounts are stacked, i.e. the one
//! on top). A target that does not exist is walked up to its nearest existing ancestor. The mount
//! is resolved once, at registration, and logged.
//!
//! The shared collector's `Free space on disk` reports `statvfs("/")` (it mirrors the managed
//! `UnixDiskInfo`, which hard-codes the root mount). Where `/srv/docker` lives on the root
//! filesystem — garage-server — both sensors describe the same mount; where it does not, these two
//! describe the Docker mount and the probe logs that the two differ.
//!
//! # Values
//!
//! `statvfs(3)` of that mount point, as `df` shows them to a non-root user: free space
//! `f_bavail / f_blocks`, free inodes `f_favail / f_files`, in percent, two decimals. Nothing else
//! is ever touched — in particular no other mount is stat'ed, so sleeping archive disks stay asleep.
//! A filesystem without an inode count (`f_files == 0`, e.g. btrfs) gets no inode sensor.

use std::path::{Path, PathBuf};
use std::time::Duration;

use hsm_collector::{
    instant_hourly_schedule_anchor, Alert, AlertCombination, AlertDestination, AlertIcon,
    AlertKind, AlertOperation, AlertProperty, AlertRepeat, AlertTarget, Collector, DoubleSensor,
    SensorOptions,
};

use super::{FailureLog, HostEnvironment, Source};
use crate::logging::Logger;

pub const DEFAULT_TARGET: &str = "/srv/docker";
pub const FREE_SPACE_PATH: &str = ".computer/Disks monitoring/Free space on disk %";
pub const FREE_INODES_PATH: &str = ".computer/Disks monitoring/Free inodes %";

const PERIOD: Duration = Duration::from_secs(300);
/// `Unit.Percents` in the managed `Unit` enum.
const UNIT_PERCENTS: i32 = 100;

/// The `statvfs` fields the sensors use.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct FsStats {
    pub blocks: u64,
    pub blocks_available: u64,
    pub files: u64,
    pub files_available: u64,
}

impl FsStats {
    /// `f_bavail / f_blocks` in percent: the share an unprivileged writer can still use.
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

/// One `/proc/self/mountinfo` entry, the fields this module uses.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Mount {
    pub mount_point: PathBuf,
    pub fs_type: String,
    pub source: String,
}

/// Parse `/proc/self/mountinfo` (proc(5)): `id parent maj:min root mount_point options
/// [optional…] - fs_type source super_options`. Malformed lines are skipped.
pub fn parse_mountinfo(text: &str) -> Vec<Mount> {
    text.lines()
        .filter_map(|line| {
            let (before, after) = line.split_once(" - ")?;
            let mount_point = before.split(' ').nth(4)?;
            let mut after = after.split(' ');
            let fs_type = after.next()?;
            let source = after.next().unwrap_or("");
            Some(Mount {
                mount_point: PathBuf::from(unescape_octal(mount_point)),
                fs_type: unescape_octal(fs_type),
                source: unescape_octal(source),
            })
        })
        .collect()
}

/// mountinfo escapes space, tab, newline and backslash as `\ooo`.
fn unescape_octal(field: &str) -> String {
    let bytes = field.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'\\' && index + 3 < bytes.len() {
            let digits = &bytes[index + 1..index + 4];
            if digits.iter().all(|digit| (b'0'..=b'7').contains(digit)) {
                let value = digits
                    .iter()
                    .fold(0u32, |acc, digit| acc * 8 + u32::from(digit - b'0'));
                if let Ok(value) = u8::try_from(value) {
                    out.push(value);
                    index += 4;
                    continue;
                }
            }
        }
        out.push(bytes[index]);
        index += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// The mount holding `target` (already canonical): the longest component-wise prefix, the last
/// one listed when several share that mount point (the top of a stack).
pub fn mount_containing<'m>(mounts: &'m [Mount], target: &Path) -> Option<&'m Mount> {
    mounts
        .iter()
        .filter(|mount| target.starts_with(&mount.mount_point))
        .fold(None, |best: Option<&Mount>, mount| match best {
            Some(current)
                if current.mount_point.components().count()
                    > mount.mount_point.components().count() =>
            {
                Some(current)
            }
            _ => Some(mount),
        })
}

/// `target` canonicalized, or its nearest existing ancestor when it does not exist.
fn existing_canonical(target: &Path) -> Option<PathBuf> {
    let mut candidate = Some(target);
    while let Some(path) = candidate {
        if let Ok(canonical) = std::fs::canonicalize(path) {
            return Some(canonical);
        }
        candidate = path.parent();
    }
    None
}

pub fn register<'c>(
    collector: &'c Collector,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    let target = &environment.disk_target;
    let Some(canonical) = existing_canonical(target) else {
        logger.error(format!(
            "disk sensors are not registered: cannot resolve {}",
            target.display()
        ));
        return None;
    };
    let mountinfo = match std::fs::read_to_string(&environment.mountinfo) {
        Ok(text) => text,
        Err(error) => {
            logger.error(format!(
                "disk sensors are not registered: cannot read {}: {error}",
                environment.mountinfo.display()
            ));
            return None;
        }
    };
    let mounts = parse_mountinfo(&mountinfo);
    let Some(mount) = mount_containing(&mounts, &canonical).cloned() else {
        logger.error(format!(
            "disk sensors are not registered: no mount in {} contains {}",
            environment.mountinfo.display(),
            canonical.display()
        ));
        return None;
    };
    logger.info(format!(
        "disk sensors: {} is on {} ({} {}){}",
        target.display(),
        mount.mount_point.display(),
        mount.fs_type,
        mount.source,
        if mount.mount_point == Path::new("/") {
            " - the same mount the collector's 'Free space on disk' reports"
        } else {
            " - NOT the root mount the collector's 'Free space on disk' reports"
        }
    ));

    // One stat up front decides whether the filesystem counts inodes at all.
    let inodes_counted = match (environment.statvfs)(&mount.mount_point) {
        Ok(stats) => stats.files > 0,
        // Keep the sensor: the failure may be transient, and sampling reports it.
        Err(_) => true,
    };

    let where_ = format!(
        "the filesystem holding {} (mount {}, {})",
        target.display(),
        mount.mount_point.display(),
        mount.fs_type
    );
    let free_space = register_percent_sensor(
        collector,
        logger,
        FREE_SPACE_PATH,
        format!(
            "Free space on {where_}, in percent: statvfs f_bavail / f_blocks, what `df` shows a \
             non-root user. Every 5 minutes."
        ),
        &[(Band::Warning, "10", Some("5")), (Band::Error, "5", None)],
    );
    let free_inodes = if inodes_counted {
        register_percent_sensor(
            collector,
            logger,
            FREE_INODES_PATH,
            format!(
                "Free inodes on {where_}, in percent: statvfs f_favail / f_files. Running out \
                 blocks file creation while space is still free. Every 5 minutes."
            ),
            &[(Band::Warning, "10", None)],
        )
    } else {
        logger.info(format!(
            "{FREE_INODES_PATH} is not registered: {} ({}) reports no inode count",
            mount.mount_point.display(),
            mount.fs_type
        ));
        None
    };

    if free_space.is_none() && free_inodes.is_none() {
        return None;
    }
    Some(Box::new(DiskSource {
        mount_point: mount.mount_point,
        statvfs: environment.statvfs,
        free_space,
        free_inodes,
        failures: FailureLog::default(),
    }))
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum Band {
    Warning,
    Error,
}

/// A percent sensor with one alert per band: `(band, below, not_below)` fires while
/// `value < below` (and `value >= not_below`, so the warning stays quiet once the error band
/// takes over).
fn register_percent_sensor<'c>(
    collector: &'c Collector,
    logger: &Logger,
    path: &'static str,
    description: String,
    bands: &[(Band, &str, Option<&str>)],
) -> Option<DoubleSensor<'c>> {
    let options = SensorOptions::default()
        .with_is_computer_sensor(true)
        .with_unit(UNIT_PERCENTS)
        .with_description(description);
    let sensor = match collector.double_sensor(path, &options) {
        Ok(sensor) => sensor,
        Err(error) => {
            logger.error(format!("cannot register {path}: {error}"));
            return None;
        }
    };
    for (band, below, not_below) in bands {
        let attached = percent_alert(collector, *band, below, *not_below)
            .and_then(|a| sensor.attach_alert(&a));
        if let Err(error) = attached {
            logger.error(format!(
                "cannot attach the {band:?} alert to {path}: {error}"
            ));
        }
    }
    Some(sensor)
}

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

struct DiskSource<'c> {
    mount_point: PathBuf,
    statvfs: fn(&Path) -> std::io::Result<FsStats>,
    free_space: Option<DoubleSensor<'c>>,
    free_inodes: Option<DoubleSensor<'c>>,
    failures: FailureLog,
}

impl Source for DiskSource<'_> {
    fn name(&self) -> &'static str {
        "disk"
    }

    fn period(&self) -> Duration {
        PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        let what = "disk sensors";
        let stats = match (self.statvfs)(&self.mount_point) {
            Ok(stats) => stats,
            Err(error) => {
                self.failures.failed(
                    logger,
                    what,
                    &format!("statvfs({}) failed: {error}", self.mount_point.display()),
                );
                return;
            }
        };
        let readings = [
            (
                &self.free_space,
                stats.free_space_percent(),
                FREE_SPACE_PATH,
            ),
            (
                &self.free_inodes,
                stats.free_inodes_percent(),
                FREE_INODES_PATH,
            ),
        ];
        let mut failure = None;
        for (sensor, value, path) in readings {
            let Some(sensor) = sensor else { continue };
            match value {
                Some(percent) => {
                    if let Err(error) = sensor.add(percent) {
                        logger.error(format!("cannot post {path}: {error}"));
                    }
                }
                None => {
                    failure = Some(format!(
                        "{} reports a zero total for {path}",
                        self.mount_point.display()
                    ))
                }
            }
        }
        match failure {
            Some(reason) => self.failures.failed(logger, what, &reason),
            None => self.failures.succeeded(logger, what),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const GARAGE: &str = "\
22 1 8:33 / / rw,relatime shared:1 - ext4 /dev/sdc1 rw,errors=remount-ro
23 22 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw
41 22 8:1 / /mnt/wd4tb rw,relatime shared:30 - ext4 /dev/sda1 rw
42 22 8:17 / /mnt/mediacentr rw,relatime shared:31 - ext4 /dev/sdb1 rw
43 22 0:45 / /srv/docker\\040data rw,relatime shared:40 - xfs /dev/sdd1 rw
44 22 0:46 / /srv rw,relatime shared:41 - tmpfs tmpfs rw
45 44 0:47 / /srv rw,relatime shared:42 - ext4 /dev/sde1 rw
";

    #[test]
    fn mountinfo_parses_fields_and_escapes() {
        let mounts = parse_mountinfo(GARAGE);
        assert_eq!(mounts.len(), 7);
        assert_eq!(mounts[0].mount_point, PathBuf::from("/"));
        assert_eq!(mounts[0].fs_type, "ext4");
        assert_eq!(mounts[0].source, "/dev/sdc1");
        assert_eq!(mounts[4].mount_point, PathBuf::from("/srv/docker data"));
        assert!(parse_mountinfo("garbage without separator\n").is_empty());
    }

    #[test]
    fn the_longest_prefix_wins_by_component_not_by_string() {
        let mounts = parse_mountinfo(GARAGE);
        // "/srv/docker" is under "/srv" (the top of the two stacked /srv mounts), not under
        // "/srv/docker data" — a plain string prefix would get this wrong.
        let mount = mount_containing(&mounts, Path::new("/srv/docker")).expect("mount");
        assert_eq!(mount.source, "/dev/sde1");
        let mount = mount_containing(&mounts, Path::new("/srv/docker data/x")).expect("mount");
        assert_eq!(mount.fs_type, "xfs");
        let mount = mount_containing(&mounts, Path::new("/var/lib")).expect("mount");
        assert_eq!(mount.mount_point, PathBuf::from("/"));
    }

    #[test]
    fn percentages_are_what_df_shows_and_never_divide_by_zero() {
        // The garage-server root filesystem on the day this was written: 47169464 of 108897780
        // 1K-blocks available to users; 5792029 of 6955008 inodes free.
        let stats = FsStats {
            blocks: 108_897_780,
            blocks_available: 47_169_464,
            files: 6_955_008,
            files_available: 5_792_029,
        };
        assert_eq!(stats.free_space_percent(), Some(43.32));
        assert_eq!(stats.free_inodes_percent(), Some(83.28));
        let empty = FsStats {
            blocks: 0,
            blocks_available: 0,
            files: 0,
            files_available: 0,
        };
        assert_eq!(empty.free_space_percent(), None);
        assert_eq!(empty.free_inodes_percent(), None);
    }

    #[test]
    fn a_missing_target_resolves_to_its_nearest_existing_ancestor() {
        let root = std::env::temp_dir();
        let missing = root.join("hsm-probe-no-such-dir/deeper");
        assert_eq!(
            existing_canonical(&missing),
            Some(std::fs::canonicalize(&root).unwrap())
        );
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn statvfs_reads_the_root_filesystem() {
        let stats = statvfs(Path::new("/")).expect("statvfs /");
        assert!(stats.blocks > 0);
        assert!(stats.blocks_available <= stats.blocks);
    }
}
