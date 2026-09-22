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
}

impl fmt::Display for SecretError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            // Only the path is interpolated — never the file's contents.
            SecretError::Read { path, source } => {
                write!(f, "cannot read the access key from {path}: {source}")
            }
            SecretError::Empty { path } => write!(f, "the access-key file {path} is empty"),
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

/// Whether the key file is readable by anyone other than its owner.
///
/// Returns `None` when the mode cannot be determined (non-Unix, or a stat failure).
#[cfg(unix)]
pub fn is_world_or_group_readable(path: &Path) -> Option<bool> {
    use std::os::unix::fs::PermissionsExt;
    let mode = std::fs::metadata(path).ok()?.permissions().mode();
    Some(mode & 0o077 != 0)
}

#[cfg(not(unix))]
pub fn is_world_or_group_readable(_path: &Path) -> Option<bool> {
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

    #[test]
    fn read_errors_mention_the_path_only() {
        let error =
            Secret::read_from_file(Path::new("/nonexistent/hsm-probe-key")).expect_err("must fail");
        let rendered = error.to_string();
        assert!(rendered.contains("/nonexistent/hsm-probe-key"));
    }
}
