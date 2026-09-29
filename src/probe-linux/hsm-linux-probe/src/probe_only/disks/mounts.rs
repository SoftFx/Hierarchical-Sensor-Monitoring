//! Which filesystems the disk sensors report, and under which name. Pure: text in, decisions out.
//!
//! * **Real filesystems only** — block-backed types ([`BLOCK_FS_TYPES`]). Pseudo filesystems
//!   (proc, sysfs, cgroup, tmpfs, devtmpfs, overlay, squashfs, autofs, …) and network ones (nfs,
//!   cifs) are not in the list and are skipped.
//! * **One node per filesystem** — every mount of one source device (bind mounts, sub-directory
//!   mounts, the read-only re-mounts systemd adds in the service's namespace) collapses into one
//!   [`Filesystem`]. The reported mount point is a whole-filesystem mount (`root` field `/`) when
//!   there is one, then the shortest (fewest components, then lexicographic).
//! * **Names** follow the Windows per-drive naming (`Free space on C disk` →
//!   `Free space on <name> disk`): `root` for `/`, else the last path segment. Names that collide
//!   (with each other, with a name already in use, or a non-root mount whose last segment is
//!   `root`) use the whole mount path with `/` → `_` instead, plus a counter if even that is taken.
//!   A name belongs to a mount point and, once given, is kept — across restarts too: the caller
//!   persists the mount point → name map, so a collision that appears later never renames a
//!   filesystem that already has history.

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};

/// Filesystem types backed by a block device.
pub const BLOCK_FS_TYPES: &[&str] = &[
    "ext2", "ext3", "ext4", "xfs", "btrfs", "vfat", "exfat", "ntfs3", "fuseblk", "f2fs",
];

/// One `/proc/self/mountinfo` entry, the fields this module uses.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Mount {
    /// `major:minor` of the device (`st_dev`).
    pub device: String,
    /// The directory of the filesystem this mount exposes (`/` for the whole filesystem).
    pub root: String,
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
            let mut fields = before.split(' ');
            let device = fields.nth(2)?;
            let root = fields.next()?;
            let mount_point = fields.next()?;
            let mut after = after.split(' ');
            let fs_type = after.next()?;
            let source = after.next().unwrap_or("");
            Some(Mount {
                device: device.to_string(),
                root: unescape_octal(root),
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

/// One real filesystem, however many times it is mounted.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Filesystem {
    /// The dedup key: the source device (`/dev/sda2`), or `major:minor` for a source that is not a
    /// device path.
    pub key: String,
    /// The mount point the sensors `statvfs` and name.
    pub mount_point: PathBuf,
    /// Every mount point of this filesystem, sorted.
    pub mount_points: Vec<PathBuf>,
    pub fs_type: String,
    pub source: String,
    /// `major:minor` of the reported mount.
    pub device: String,
}

/// The real filesystems in `mounts`, deduplicated by source device, minus those whose reported
/// mount point matches an `exclude` pattern. Sorted by mount point.
pub fn real_filesystems(mounts: &[Mount], exclude: &[String]) -> Vec<Filesystem> {
    let mut groups: BTreeMap<String, Vec<&Mount>> = BTreeMap::new();
    for mount in mounts
        .iter()
        .filter(|mount| BLOCK_FS_TYPES.contains(&mount.fs_type.as_str()))
    {
        let key = if mount.source.starts_with("/dev/") {
            mount.source.clone()
        } else {
            mount.device.clone()
        };
        groups.entry(key).or_default().push(mount);
    }

    let mut filesystems: Vec<Filesystem> = groups
        .into_iter()
        .filter_map(|(key, group)| {
            let best = group.iter().min_by(|a, b| {
                (a.root != "/")
                    .cmp(&(b.root != "/"))
                    .then(
                        a.mount_point
                            .components()
                            .count()
                            .cmp(&b.mount_point.components().count()),
                    )
                    .then(a.mount_point.cmp(&b.mount_point))
            })?;
            let mount_points: BTreeSet<PathBuf> = group
                .iter()
                .map(|mount| mount.mount_point.clone())
                .collect();
            Some(Filesystem {
                key,
                mount_point: best.mount_point.clone(),
                mount_points: mount_points.into_iter().collect(),
                fs_type: best.fs_type.clone(),
                source: best.source.clone(),
                device: best.device.clone(),
            })
        })
        .filter(|fs| {
            let point = fs.mount_point.to_string_lossy();
            !exclude.iter().any(|pattern| glob_matches(pattern, &point))
        })
        .collect();
    filesystems.sort_by(|a, b| a.mount_point.cmp(&b.mount_point));
    filesystems
}

/// `*` matches any run of characters (including `/`); everything else matches itself.
pub fn glob_matches(pattern: &str, text: &str) -> bool {
    let pattern: Vec<char> = pattern.chars().collect();
    let text: Vec<char> = text.chars().collect();
    // Iterative wildcard match with backtracking to the last `*`.
    let (mut p, mut t) = (0usize, 0usize);
    let (mut star, mut mark) = (None::<usize>, 0usize);
    while t < text.len() {
        if p < pattern.len() && pattern[p] != '*' && pattern[p] == text[t] {
            p += 1;
            t += 1;
        } else if p < pattern.len() && pattern[p] == '*' {
            star = Some(p);
            mark = t;
            p += 1;
        } else if let Some(star_at) = star {
            p = star_at + 1;
            mark += 1;
            t = mark;
        } else {
            return false;
        }
    }
    while p < pattern.len() && pattern[p] == '*' {
        p += 1;
    }
    p == pattern.len()
}

/// The natural name of a mount point: `root` for `/`, else its last segment.
fn natural_name(mount_point: &Path) -> String {
    if mount_point == Path::new("/") {
        return "root".to_string();
    }
    mount_point
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| full_path_name(mount_point))
}

/// The collision fallback: the whole mount path with `/` → `_` (`/srv/data` → `_srv_data`).
fn full_path_name(mount_point: &Path) -> String {
    mount_point.to_string_lossy().replace('/', "_")
}

/// Give every filesystem in `filesystems` without a name in `names` (mount point → name) a name,
/// keeping the names already given — `names` is persisted by the caller, so a name survives
/// restarts too. See the module docs for the rule.
pub fn assign_names(filesystems: &[Filesystem], names: &mut BTreeMap<String, String>) {
    let new: Vec<&Filesystem> = filesystems
        .iter()
        .filter(|fs| !names.contains_key(&name_key(fs)))
        .collect();
    let mut taken: BTreeSet<String> = names.values().cloned().collect();
    let mut candidates: BTreeMap<String, usize> = BTreeMap::new();
    for fs in &new {
        *candidates.entry(natural_name(&fs.mount_point)).or_default() += 1;
    }
    for fs in new {
        let natural = natural_name(&fs.mount_point);
        let is_root = fs.mount_point == Path::new("/");
        let collides =
            candidates[&natural] > 1 || taken.contains(&natural) || (!is_root && natural == "root");
        let mut name = if collides && !is_root {
            full_path_name(&fs.mount_point)
        } else {
            natural
        };
        // The fallback can itself collide (`/mnt/_srv_data` vs `/srv/data`): two filesystems must
        // never share a sensor, so a taken name gets a counter.
        if taken.contains(&name) {
            let base = name.clone();
            let mut counter = 2;
            while taken.contains(&name) {
                name = format!("{base}_{counter}");
                counter += 1;
            }
        }
        taken.insert(name.clone());
        names.insert(name_key(fs), name);
    }
}

/// Names follow the mount point, not the device: device names (`/dev/sdb1`) can swap between boots,
/// the mount point is what the operator named and what the sensor describes.
pub fn name_key(fs: &Filesystem) -> String {
    fs.mount_point.to_string_lossy().into_owned()
}

#[cfg(test)]
pub mod tests {
    use super::*;

    /// `/proc/<probe pid>/mountinfo` as the hsm-linux-probe service sees it on garage-server
    /// (2026-09-29): its own namespace, with the read-only re-mounts and bind mounts systemd adds,
    /// the FUSE-NTFS archives mounted three times each, and Docker overlays (two kept of eleven).
    pub const GARAGE_MOUNTINFO: &str = include_str!("fixtures/garage-mountinfo.txt");

    fn garage() -> Vec<Filesystem> {
        real_filesystems(&parse_mountinfo(GARAGE_MOUNTINFO), &[])
    }

    #[test]
    fn garage_has_four_real_filesystems_one_per_device() {
        let filesystems = garage();
        let summary: Vec<(String, String, String)> = filesystems
            .iter()
            .map(|fs| {
                (
                    fs.key.clone(),
                    fs.mount_point.to_string_lossy().into_owned(),
                    fs.fs_type.clone(),
                )
            })
            .collect();
        assert_eq!(
            summary,
            vec![
                ("/dev/sdc1".into(), "/".into(), "ext4".into()),
                (
                    "/dev/sdb1".into(),
                    "/mnt/mediacentr".into(),
                    "fuseblk".into()
                ),
                ("/dev/sdb5".into(), "/mnt/oldlinux".into(), "ext4".into()),
                ("/dev/sda2".into(), "/mnt/wd4tb".into(), "fuseblk".into()),
            ]
        );
        // The root filesystem also backs the service's StateDirectory/LogsDirectory bind mounts.
        let root = &filesystems[0];
        assert!(root
            .mount_points
            .contains(&PathBuf::from("/var/lib/hsm-linux-probe")));
        // /mnt/.rw/wd4tb (3 segments) and /mnt/wd4tb/backup (a sub-directory mount) lose to
        // /mnt/wd4tb.
        assert_eq!(filesystems[3].mount_points.len(), 3);
        assert_eq!(filesystems[3].device, "8:2");
    }

    #[test]
    fn pseudo_and_network_filesystems_are_skipped() {
        let text = "\
1 0 0:5 / /proc rw - proc proc rw
2 0 0:6 / /tmp rw - tmpfs tmpfs rw
3 0 0:7 / /var/lib/docker/overlay2/x/merged rw - overlay overlay rw
4 0 0:8 / /snap/core/1 ro - squashfs /dev/loop0 ro
5 0 0:9 / /net rw - nfs4 server:/export rw
6 0 0:10 / /smb rw - cifs //server/share rw
7 0 0:11 / /auto rw - autofs systemd-1 rw
8 0 0:12 / /sys/fs/cgroup rw - cgroup2 cgroup2 rw
9 0 8:1 / /data rw - xfs /dev/sdd1 rw
";
        let filesystems = real_filesystems(&parse_mountinfo(text), &[]);
        assert_eq!(filesystems.len(), 1);
        assert_eq!(filesystems[0].mount_point, PathBuf::from("/data"));
    }

    #[test]
    fn a_whole_filesystem_mount_beats_a_shorter_sub_directory_mount() {
        let text = "\
1 0 8:1 /sub /a rw - ext4 /dev/sdd1 rw
2 0 8:1 / /data/disk rw - ext4 /dev/sdd1 rw
";
        let filesystems = real_filesystems(&parse_mountinfo(text), &[]);
        assert_eq!(filesystems[0].mount_point, PathBuf::from("/data/disk"));
    }

    #[test]
    fn a_source_that_is_not_a_device_path_dedupes_by_device_number() {
        let text = "\
1 0 0:40 / /pool rw - btrfs pool rw
2 0 0:40 / /mnt/pool rw - btrfs pool rw
3 0 0:41 / /other rw - btrfs other rw
";
        let filesystems = real_filesystems(&parse_mountinfo(text), &[]);
        let keys: Vec<&str> = filesystems.iter().map(|fs| fs.key.as_str()).collect();
        assert_eq!(keys, vec!["0:41", "0:40"]);
        assert_eq!(filesystems[1].mount_point, PathBuf::from("/pool"));
    }

    #[test]
    fn exclude_patterns_match_the_reported_mount_point() {
        let mounts = parse_mountinfo(GARAGE_MOUNTINFO);
        let filesystems = real_filesystems(&mounts, &["/mnt/wd4*".into(), "/mnt/oldlinux".into()]);
        let points: Vec<String> = filesystems
            .iter()
            .map(|fs| fs.mount_point.to_string_lossy().into_owned())
            .collect();
        assert_eq!(points, vec!["/", "/mnt/mediacentr"]);
    }

    #[test]
    fn globs() {
        assert!(glob_matches("/mnt/*", "/mnt/wd4tb"));
        assert!(glob_matches("/mnt/*", "/mnt/a/b"));
        assert!(glob_matches("*", "/"));
        assert!(glob_matches("/mnt/*tb", "/mnt/wd4tb"));
        assert!(glob_matches("/mnt/wd4tb", "/mnt/wd4tb"));
        assert!(!glob_matches("/mnt/wd4tb", "/mnt/wd4tb2"));
        assert!(!glob_matches("/mnt/*/x", "/mnt/a"));
        assert!(glob_matches("*a*b*", "xaybz"));
    }

    #[test]
    fn garage_names_are_root_and_last_segments() {
        let mut names = BTreeMap::new();
        assign_names(&garage(), &mut names);
        assert_eq!(names["/"], "root");
        assert_eq!(names["/mnt/wd4tb"], "wd4tb");
        assert_eq!(names["/mnt/mediacentr"], "mediacentr");
        assert_eq!(names["/mnt/oldlinux"], "oldlinux");
        assert_eq!(names.len(), 4);
    }

    #[test]
    fn a_fallback_name_that_is_taken_gets_a_counter() {
        // `/mnt/_srv_data` is named `_srv_data` first; later `/srv/data` and `/mnt/data` collide
        // and fall back to their whole paths — `_srv_data` for `/srv/data` is taken.
        let first = "1 0 8:1 / /mnt/_srv_data rw - ext4 /dev/sda1 rw\n";
        let mut names = BTreeMap::new();
        let mut all = real_filesystems(&parse_mountinfo(first), &[]);
        assign_names(&all, &mut names);
        let later = "\
2 0 8:2 / /srv/data rw - ext4 /dev/sda2 rw
3 0 8:3 / /mnt/data rw - ext4 /dev/sda3 rw
";
        all.extend(real_filesystems(&parse_mountinfo(later), &[]));
        assign_names(&all, &mut names);
        assert_eq!(names["/mnt/_srv_data"], "_srv_data");
        assert_eq!(names["/srv/data"], "_srv_data_2");
        assert_eq!(names["/mnt/data"], "_mnt_data");
        let unique: BTreeSet<&String> = names.values().collect();
        assert_eq!(unique.len(), names.len(), "no two filesystems share a name");
    }

    #[test]
    fn colliding_names_use_the_whole_path_and_given_names_stay() {
        let text = "\
1 0 8:1 / / rw - ext4 /dev/sda1 rw
2 0 8:2 / /srv/data rw - ext4 /dev/sda2 rw
3 0 8:3 / /mnt/data rw - ext4 /dev/sda3 rw
4 0 8:4 / /mnt/root rw - ext4 /dev/sda4 rw
";
        let filesystems = real_filesystems(&parse_mountinfo(text), &[]);
        let mut names = BTreeMap::new();
        assign_names(&filesystems, &mut names);
        assert_eq!(names["/"], "root");
        assert_eq!(names["/srv/data"], "_srv_data");
        assert_eq!(names["/mnt/data"], "_mnt_data");
        assert_eq!(names["/mnt/root"], "_mnt_root");

        // A later mount whose natural name is already taken gets the whole path; the existing
        // names do not change.
        let later = "\
5 0 8:5 / /backup/_srv_data rw - ext4 /dev/sda5 rw
6 0 8:6 / /media/usb rw - vfat /dev/sdb1 rw
";
        let mut all = filesystems.clone();
        all.extend(real_filesystems(&parse_mountinfo(later), &[]));
        assign_names(&all, &mut names);
        assert_eq!(names["/srv/data"], "_srv_data");
        assert_eq!(names["/backup/_srv_data"], "_backup__srv_data");
        assert_eq!(names["/media/usb"], "usb");
    }

    #[test]
    fn escaped_mount_points_are_unescaped() {
        let text = "1 0 8:1 / /mnt/my\\040disk rw - ext4 /dev/sdd1 rw\n";
        let filesystems = real_filesystems(&parse_mountinfo(text), &[]);
        assert_eq!(filesystems[0].mount_point, PathBuf::from("/mnt/my disk"));
        assert!(parse_mountinfo("garbage without separator\n").is_empty());
    }
}
