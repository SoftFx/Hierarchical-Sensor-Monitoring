//! Access-key handling (initiative §4.3).
//!
//! The key is read once, at startup, from a file the operator placed out of band (under systemd,
//! a `LoadCredential=` drop). It is never written to a log, never passed in argv, never stored in
//! the config file, and never included in an error message.

use std::fmt;
use std::path::Path;

/// A string that must not appear in any diagnostic.
///
/// `Debug`/`Display` render a placeholder, and the buffer is overwritten on drop. Zeroization is
/// best-effort: the collector copies the key into its own C++ storage, and an allocator or the
/// kernel may have kept an earlier copy. It still removes the obvious in-process residue.
pub struct Secret(String);

impl Secret {
    pub fn expose(&self) -> &str {
        &self.0
    }

    /// Read a key from a file, trimming surrounding whitespace (editors add a trailing newline).
    pub fn read_from_file(path: &Path) -> Result<Self, SecretError> {
        let mut raw = std::fs::read_to_string(path).map_err(|source| SecretError::Read {
            path: path.display().to_string(),
            source,
        })?;
        let key = raw.trim().to_string();
        // The read buffer holds the key too; wipe it rather than leaving a second copy behind on
        // the freed heap.
        wipe(&mut raw);

        if key.is_empty() {
            return Err(SecretError::Empty {
                path: path.display().to_string(),
            });
        }
        Ok(Secret(key))
    }
}

impl Drop for Secret {
    fn drop(&mut self) {
        // SAFETY: the bytes are overwritten with zeros, which is valid UTF-8, and the String is
        // dropped immediately afterwards. write_volatile keeps the compiler from eliding the wipe.
        unsafe {
            let bytes = self.0.as_mut_vec();
            for byte in bytes.iter_mut() {
                std::ptr::write_volatile(byte, 0);
            }
        }
    }
}

impl fmt::Debug for Secret {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("Secret(<redacted>)")
    }
}

impl fmt::Display for Secret {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("<redacted>")
    }
}

#[derive(Debug)]
pub enum SecretError {
    Read {
        path: String,
        source: std::io::Error,
    },
    Empty {
        path: String,
    },
    NoCredentialsDirectory {
        path: String,
    },
}

impl fmt::Display for SecretError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            // Only the path is interpolated — never the file's contents.
            SecretError::Read { path, source } => {
                write!(f, "cannot read the access key from {path}: {source}")
            }
            SecretError::Empty { path } => write!(f, "the access-key file {path} is empty"),
            SecretError::NoCredentialsDirectory { path } => write!(
                f,
                "hsm.accessKeyFile '{path}' is relative, which names a systemd LoadCredential= \
                 credential, but $CREDENTIALS_DIRECTORY is not set; run under the unit or use an \
                 absolute path"
            ),
        }
    }
}

impl std::error::Error for SecretError {}

/// Overwrite a string's bytes in place and clear it.
///
/// Used for the transient copy of the key that [`crate::probe`] hands to the collector options;
/// same best-effort caveat as [`Secret`]'s drop.
pub fn wipe(value: &mut String) {
    // SAFETY: zeros are valid UTF-8, and the buffer is truncated to empty right after.
    unsafe {
        let bytes = value.as_mut_vec();
        for byte in bytes.iter_mut() {
            std::ptr::write_volatile(byte, 0);
        }
        bytes.clear();
    }
}

/// Resolve the configured key path.
///
/// An absolute path is used as is. A relative one names a credential in systemd's
/// `$CREDENTIALS_DIRECTORY` — the directory `LoadCredential=` fills, whose exact path embeds the
/// unit name (`/run/credentials/<unit>/`) — so the config never has to hardcode it. A relative path
/// without that variable is an error rather than a silent lookup in the working directory.
pub fn resolve_key_path(
    configured: &Path,
    credentials_directory: Option<&std::ffi::OsStr>,
) -> Result<std::path::PathBuf, SecretError> {
    if configured.is_absolute() {
        return Ok(configured.to_path_buf());
    }
    match credentials_directory.filter(|dir| !dir.is_empty()) {
        Some(dir) => Ok(Path::new(dir).join(configured)),
        None => Err(SecretError::NoCredentialsDirectory {
            path: configured.display().to_string(),
        }),
    }
}

/// POSIX ACL entry tags (`<linux/posix_acl.h>`).
const ACL_USER_OBJ: u16 = 0x01;
const ACL_USER: u16 = 0x02;
const ACL_GROUP_OBJ: u16 = 0x04;
const ACL_GROUP: u16 = 0x08;
const ACL_MASK: u16 = 0x10;
const ACL_OTHER: u16 = 0x20;
const ACL_READ: u16 = 0x04;

/// One entry of a POSIX access ACL.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct AclEntry {
    pub tag: u16,
    pub perm: u16,
    pub id: u32,
}

/// Parse the `system.posix_acl_access` extended attribute: a little-endian `u32` version (2)
/// followed by 8-byte `{u16 tag, u16 perm, u32 id}` entries. `None` for anything malformed.
pub fn parse_posix_acl(bytes: &[u8]) -> Option<Vec<AclEntry>> {
    let (header, body) = bytes.split_at_checked(4)?;
    if u32::from_le_bytes(header.try_into().ok()?) != 2 || body.len() % 8 != 0 {
        return None;
    }
    Some(
        body.chunks_exact(8)
            .map(|chunk| AclEntry {
                tag: u16::from_le_bytes([chunk[0], chunk[1]]),
                perm: u16::from_le_bytes([chunk[2], chunk[3]]),
                id: u32::from_le_bytes([chunk[4], chunk[5], chunk[6], chunk[7]]),
            })
            .collect(),
    )
}

/// Whether anyone other than the file's owner and this process's own user can read the key.
///
/// Mode bits alone get this wrong for the supported deployment. systemd's `write_credential()`
/// creates a `LoadCredential=` file root-owned `0400` and then grants the service user read via a
/// named-user ACL entry (`user:<uid>:r--`). Adding that entry sets the ACL mask to `r--`, and the
/// kernel reports the mask in the group bits, so `stat` shows `0440` although `group::---`. With
/// an ACL present, the group bits are the mask, not the owning group's permission; the real
/// answer is in the entries.
///
/// Readable beyond the owner means: `other` has read; or (no ACL) the group bits have read; or
/// (ACL) the owning group, a named group, or a named user other than `self_uid` has read after
/// the mask is applied.
pub fn readable_beyond_owner(mode: u32, acl: Option<&[AclEntry]>, self_uid: u32) -> bool {
    if mode & 0o004 != 0 {
        return true;
    }
    let Some(entries) = acl else {
        return mode & 0o040 != 0;
    };
    let mask = entries
        .iter()
        .find(|entry| entry.tag == ACL_MASK)
        .map_or(0o7, |entry| entry.perm);
    entries.iter().any(|entry| {
        let effective = entry.perm & mask;
        match entry.tag {
            ACL_GROUP_OBJ | ACL_GROUP => effective & ACL_READ != 0,
            ACL_USER => entry.id != self_uid && effective & ACL_READ != 0,
            ACL_OTHER => entry.perm & ACL_READ != 0,
            ACL_USER_OBJ | ACL_MASK => false,
            _ => false,
        }
    })
}

/// Whether the key file is readable beyond its owner and this service's user (see
/// [`readable_beyond_owner`]). `None` when it cannot be determined (non-Unix, or a stat failure).
#[cfg(unix)]
pub fn is_readable_beyond_owner(path: &Path) -> Option<bool> {
    use std::os::unix::fs::PermissionsExt;
    let mode = std::fs::metadata(path).ok()?.permissions().mode();
    let acl = read_access_acl(path);
    // SAFETY: geteuid has no preconditions and cannot fail.
    let self_uid = unsafe { libc::geteuid() };
    Some(readable_beyond_owner(mode, acl.as_deref(), self_uid))
}

#[cfg(not(unix))]
pub fn is_readable_beyond_owner(_path: &Path) -> Option<bool> {
    None
}

/// The file's access ACL, or `None` when it has none (or the filesystem has no ACL support).
#[cfg(target_os = "linux")]
fn read_access_acl(path: &Path) -> Option<Vec<AclEntry>> {
    use std::os::unix::ffi::OsStrExt;
    let c_path = std::ffi::CString::new(path.as_os_str().as_bytes()).ok()?;
    let name = c"system.posix_acl_access";
    let mut buffer = [0u8; 512];
    // SAFETY: both strings are NUL-terminated; the buffer length is passed alongside it.
    let length = unsafe {
        libc::getxattr(
            c_path.as_ptr(),
            name.as_ptr(),
            buffer.as_mut_ptr().cast(),
            buffer.len(),
        )
    };
    if length <= 0 {
        return None;
    }
    parse_posix_acl(&buffer[..usize::try_from(length).ok()?])
}

#[cfg(all(unix, not(target_os = "linux")))]
fn read_access_acl(_path: &Path) -> Option<Vec<AclEntry>> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_and_display_never_reveal_the_key() {
        let secret = Secret("super-secret-key".to_string());
        assert_eq!(format!("{secret:?}"), "Secret(<redacted>)");
        assert_eq!(format!("{secret}"), "<redacted>");
        assert_eq!(secret.expose(), "super-secret-key");
    }

    #[test]
    fn wiping_clears_the_buffer() {
        let mut copy = "super-secret-key".to_string();
        wipe(&mut copy);
        assert!(copy.is_empty());
    }

    #[test]
    fn reading_trims_the_trailing_newline_an_editor_leaves() {
        let path = std::env::temp_dir().join(format!("hsm-probe-key-{}", std::process::id()));
        std::fs::write(&path, "  a-product-access-key\n").expect("write");
        let secret = Secret::read_from_file(&path).expect("read");
        assert_eq!(secret.expose(), "a-product-access-key");

        std::fs::write(&path, "   \n").expect("write");
        assert!(matches!(
            Secret::read_from_file(&path),
            Err(SecretError::Empty { .. })
        ));
        let _ = std::fs::remove_file(&path);
    }

    /// The ACL systemd leaves on a `LoadCredential=` file for `User=hsm-probe` (uid 994), observed
    /// with getfacl in a debian:13 container after reproducing `write_credential()`: file
    /// `root:root`, `stat` mode `0440` (the mask), `user::r-- user:994:r-- group::--- mask::r--
    /// other::---`.
    fn systemd_credential_acl(service_uid: u32) -> Vec<AclEntry> {
        vec![
            AclEntry {
                tag: ACL_USER_OBJ,
                perm: 0o4,
                id: u32::MAX,
            },
            AclEntry {
                tag: ACL_USER,
                perm: 0o4,
                id: service_uid,
            },
            AclEntry {
                tag: ACL_GROUP_OBJ,
                perm: 0o0,
                id: u32::MAX,
            },
            AclEntry {
                tag: ACL_MASK,
                perm: 0o4,
                id: u32::MAX,
            },
            AclEntry {
                tag: ACL_OTHER,
                perm: 0o0,
                id: u32::MAX,
            },
        ]
    }

    #[test]
    fn a_systemd_credential_is_not_readable_beyond_the_service() {
        // The trial's false positive: mode 0440 alone looks group-readable.
        let acl = systemd_credential_acl(994);
        assert!(!readable_beyond_owner(0o100440, Some(&acl), 994));
    }

    #[test]
    fn a_named_user_other_than_the_service_is_flagged() {
        let acl = systemd_credential_acl(1000);
        assert!(readable_beyond_owner(0o100440, Some(&acl), 994));
    }

    #[test]
    fn genuinely_group_or_world_readable_files_are_still_flagged() {
        // No ACL: the group bits are the owning group's permission.
        assert!(readable_beyond_owner(0o100440, None, 994));
        assert!(readable_beyond_owner(0o100404, None, 994));
        assert!(readable_beyond_owner(0o100644, None, 994));
        assert!(!readable_beyond_owner(0o100400, None, 994));
        assert!(!readable_beyond_owner(0o100600, None, 994));

        // ACL granting the owning group read.
        let mut acl = systemd_credential_acl(994);
        acl[2].perm = 0o4;
        assert!(readable_beyond_owner(0o100440, Some(&acl), 994));

        // A named group with read.
        let mut acl = systemd_credential_acl(994);
        acl.push(AclEntry {
            tag: ACL_GROUP,
            perm: 0o4,
            id: 50,
        });
        assert!(readable_beyond_owner(0o100440, Some(&acl), 994));

        // World-readable wins regardless of the ACL.
        assert!(readable_beyond_owner(
            0o100444,
            Some(&systemd_credential_acl(994)),
            994
        ));
    }

    #[test]
    fn the_mask_limits_named_entries() {
        // A named group with read but a mask of --- grants nothing.
        let mut acl = systemd_credential_acl(994);
        acl.push(AclEntry {
            tag: ACL_GROUP,
            perm: 0o4,
            id: 50,
        });
        acl[3].perm = 0o0;
        assert!(!readable_beyond_owner(0o100400, Some(&acl), 994));
    }

    #[test]
    fn parses_the_kernel_xattr_encoding() {
        let mut bytes = 2u32.to_le_bytes().to_vec();
        for (tag, perm, id) in [
            (ACL_USER_OBJ, 4u16, u32::MAX),
            (ACL_USER, 4, 994),
            (ACL_MASK, 4, u32::MAX),
        ] {
            bytes.extend_from_slice(&tag.to_le_bytes());
            bytes.extend_from_slice(&perm.to_le_bytes());
            bytes.extend_from_slice(&id.to_le_bytes());
        }
        let entries = parse_posix_acl(&bytes).expect("parse");
        assert_eq!(
            entries[1],
            AclEntry {
                tag: ACL_USER,
                perm: 4,
                id: 994
            }
        );

        assert!(parse_posix_acl(&[]).is_none());
        assert!(
            parse_posix_acl(&1u32.to_le_bytes()).is_none(),
            "wrong version"
        );
        assert!(
            parse_posix_acl(&[2, 0, 0, 0, 1, 2, 3]).is_none(),
            "truncated entry"
        );
    }

    /// End to end on the real filesystem: write the exact ACL systemd writes, through the same
    /// xattr the kernel exposes, and check the file-level answer. Skipped where the filesystem has
    /// no ACL support (e.g. some container overlays).
    #[cfg(target_os = "linux")]
    #[test]
    fn a_file_carrying_the_systemd_credential_acl_passes_the_check() {
        use std::os::unix::ffi::OsStrExt;
        use std::os::unix::fs::PermissionsExt;

        let path = std::env::temp_dir().join(format!("hsm-probe-cred-acl-{}", std::process::id()));
        std::fs::write(&path, "k").expect("write");
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o400)).expect("chmod");

        // SAFETY: geteuid has no preconditions.
        let self_uid = unsafe { libc::geteuid() };
        let mut xattr = 2u32.to_le_bytes().to_vec();
        for (tag, perm, id) in [
            (ACL_USER_OBJ, 4u16, u32::MAX),
            (ACL_USER, 4, self_uid),
            (ACL_GROUP_OBJ, 0, u32::MAX),
            (ACL_MASK, 4, u32::MAX),
            (ACL_OTHER, 0, u32::MAX),
        ] {
            xattr.extend_from_slice(&tag.to_le_bytes());
            xattr.extend_from_slice(&perm.to_le_bytes());
            xattr.extend_from_slice(&id.to_le_bytes());
        }
        let c_path = std::ffi::CString::new(path.as_os_str().as_bytes()).expect("path");
        // SAFETY: NUL-terminated strings; the buffer length is passed alongside it.
        let rc = unsafe {
            libc::setxattr(
                c_path.as_ptr(),
                c"system.posix_acl_access".as_ptr(),
                xattr.as_ptr().cast(),
                xattr.len(),
                0,
            )
        };
        if rc != 0 {
            eprintln!(
                "skipping: no POSIX ACL support here ({})",
                std::io::Error::last_os_error()
            );
            let _ = std::fs::remove_file(&path);
            return;
        }

        let mode = std::fs::metadata(&path).expect("stat").permissions().mode();
        assert_eq!(
            mode & 0o777,
            0o440,
            "the ACL mask shows in the group bits, as on the trial host"
        );
        assert_eq!(is_readable_beyond_owner(&path), Some(false));
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn relative_key_paths_resolve_against_the_credentials_directory() {
        let dir = std::ffi::OsStr::new("/run/credentials/hsm-linux-probe.service");
        assert_eq!(
            resolve_key_path(Path::new("access-key"), Some(dir)).expect("resolve"),
            Path::new("/run/credentials/hsm-linux-probe.service/access-key")
        );
        // An absolute path is honored even under systemd.
        assert_eq!(
            resolve_key_path(Path::new("/etc/hsm/key"), Some(dir)).expect("resolve"),
            Path::new("/etc/hsm/key")
        );
        assert_eq!(
            resolve_key_path(Path::new("/etc/hsm/key"), None).expect("resolve"),
            Path::new("/etc/hsm/key")
        );
        // Relative without systemd is a clear error, not a working-directory lookup.
        for missing in [None, Some(std::ffi::OsStr::new(""))] {
            assert!(matches!(
                resolve_key_path(Path::new("access-key"), missing),
                Err(SecretError::NoCredentialsDirectory { .. })
            ));
        }
    }

    #[test]
    fn read_errors_mention_the_path_only() {
        let error =
            Secret::read_from_file(Path::new("/nonexistent/hsm-probe-key")).expect_err("must fail");
        let rendered = error.to_string();
        assert!(rendered.contains("/nonexistent/hsm-probe-key"));
    }
}
