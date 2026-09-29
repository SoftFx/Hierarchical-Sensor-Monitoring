//! The persisted mount point → name map (`$STATE_DIRECTORY/disk-names.json`).
//!
//! Collision fallbacks depend on which mounts exist when a name is first given, so without this
//! file a restart with a different set of mounts could rename a filesystem and split its history
//! on the server. Writes are atomic (temp file + fsync + rename); an unreadable or corrupt file is
//! logged and treated as empty — names are then given afresh, never guessed.

use std::collections::BTreeMap;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

use crate::logging::Logger;

pub const FILE_NAME: &str = "disk-names.json";

/// The map at `path`; empty when the file does not exist yet, or cannot be used (logged).
pub fn load(path: &Path, logger: &Logger) -> BTreeMap<String, String> {
    let text = match fs::read_to_string(path) {
        Ok(text) => text,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return BTreeMap::new(),
        Err(error) => {
            logger.error(format!(
                "disks: cannot read the disk names in {}: {error}; naming afresh",
                path.display()
            ));
            return BTreeMap::new();
        }
    };
    match serde_json::from_str(&text) {
        Ok(names) => names,
        Err(error) => {
            logger.error(format!(
                "disks: {} is not a disk-name map ({error}); naming afresh",
                path.display()
            ));
            BTreeMap::new()
        }
    }
}

/// Write atomically: `<path>.tmp`, fsync, rename over `path`.
pub fn save(path: &Path, names: &BTreeMap<String, String>) -> io::Result<()> {
    let temp = temp_path(path);
    {
        let mut file = fs::File::create(&temp)?;
        // Serializing a string map cannot fail.
        file.write_all(
            serde_json::to_string_pretty(names)
                .unwrap_or_default()
                .as_bytes(),
        )?;
        file.write_all(b"\n")?;
        file.sync_all()?;
    }
    fs::rename(&temp, path).inspect_err(|_| {
        let _ = fs::remove_file(&temp);
    })
}

fn temp_path(path: &Path) -> PathBuf {
    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(".tmp");
    path.with_file_name(name)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::logging::Level;
    use crate::probe_only::host::tests::FakeTree;

    #[test]
    fn names_round_trip_and_a_bad_file_means_naming_afresh() {
        let tree = FakeTree::new("names");
        let path = tree.0.join(FILE_NAME);
        let logger = Logger::new(Level::Error, None);
        assert!(load(&path, &logger).is_empty(), "no file yet");

        let names: BTreeMap<String, String> = [("/", "root"), ("/srv/data", "_srv_data")]
            .into_iter()
            .map(|(point, name)| (point.to_string(), name.to_string()))
            .collect();
        save(&path, &names).expect("save");
        assert_eq!(load(&path, &logger), names);
        assert!(!temp_path(&path).exists());

        tree.file(FILE_NAME, "{ not json");
        assert!(load(&path, &logger).is_empty());
    }
}
