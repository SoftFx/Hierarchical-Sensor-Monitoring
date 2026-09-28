//! The Docker source's durable memory, one JSON file on the SSD.
//!
//! Under systemd this is `$STATE_DIRECTORY/docker-state.json` (`StateDirectory=hsm-linux-probe` →
//! `/var/lib/hsm-linux-probe`, the unit's only writable path besides its logs). Per service it
//! holds what must survive a probe restart or a container recreate: the restart-count baseline, the
//! last posted restart count, the OOM latch and when the service was last seen.
//!
//! Writes are atomic (temp file + fsync + rename), so a crash mid-write leaves the previous file. A
//! missing file is a fresh start; a corrupt or unreadable one is too, logged once — losing the
//! baseline costs at most one restart-count re-post, never a crash loop.

use std::collections::BTreeMap;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use super::identity::ServiceKey;

/// Bumped on an incompatible layout change; a file of another version is treated as absent.
pub const STATE_VERSION: u32 = 1;
pub const STATE_FILE_NAME: &str = "docker-state.json";

/// Everything remembered about one service.
#[derive(Clone, Debug, Default, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ServiceRecord {
    pub project: String,
    pub service: String,
    /// The `RestartCount` last seen for each of the service's current containers.
    #[serde(default)]
    pub containers: BTreeMap<String, u64>,
    /// Cumulative restarts of the service across container recreates.
    #[serde(default)]
    pub restart_total: u64,
    /// The last value posted to `Restart count`; `None` = never posted.
    #[serde(default)]
    pub restart_posted: Option<u64>,
    /// Unix seconds until which `OOM killed` stays true; `None` = no latch.
    #[serde(default)]
    pub oom_latch_until: Option<i64>,
    /// Unix seconds of the last poll that listed a container of this service.
    #[serde(default)]
    pub last_seen: i64,
}

impl ServiceRecord {
    pub fn new(key: &ServiceKey) -> Self {
        Self {
            project: key.project.clone(),
            service: key.service.clone(),
            ..Self::default()
        }
    }

    pub fn key(&self) -> ServiceKey {
        ServiceKey::new(self.project.clone(), self.service.clone())
    }
}

#[derive(Debug, Deserialize, Serialize)]
struct StateFile {
    version: u32,
    #[serde(default)]
    services: Vec<ServiceRecord>,
}

/// The whole persisted state, keyed by service.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct State {
    pub services: BTreeMap<ServiceKey, ServiceRecord>,
}

/// How [`State::load`] went, for the one log line it deserves.
#[derive(Debug)]
pub enum LoadOutcome {
    Loaded(usize),
    Missing,
    /// Unreadable, corrupt, or another layout version: starting fresh.
    Discarded(String),
}

impl State {
    pub fn load(path: &Path) -> (State, LoadOutcome) {
        let text = match fs::read_to_string(path) {
            Ok(text) => text,
            Err(error) if error.kind() == io::ErrorKind::NotFound => {
                return (State::default(), LoadOutcome::Missing)
            }
            Err(error) => return (State::default(), LoadOutcome::Discarded(error.to_string())),
        };
        match State::parse(&text) {
            Ok(state) => {
                let count = state.services.len();
                (state, LoadOutcome::Loaded(count))
            }
            Err(reason) => (State::default(), LoadOutcome::Discarded(reason)),
        }
    }

    pub fn parse(text: &str) -> Result<State, String> {
        let file: StateFile = serde_json::from_str(text).map_err(|error| error.to_string())?;
        if file.version != STATE_VERSION {
            return Err(format!(
                "state file version {} is not {STATE_VERSION}",
                file.version
            ));
        }
        Ok(State {
            services: file
                .services
                .into_iter()
                .map(|record| (record.key(), record))
                .collect(),
        })
    }

    pub fn to_json(&self) -> String {
        let file = StateFile {
            version: STATE_VERSION,
            services: self.services.values().cloned().collect(),
        };
        // Serializing plain owned data cannot fail.
        serde_json::to_string_pretty(&file).unwrap_or_default()
    }

    /// Write atomically: `<path>.tmp`, fsync, rename over `path`.
    pub fn save(&self, path: &Path) -> io::Result<()> {
        let temp = temp_path(path);
        {
            let mut file = fs::File::create(&temp)?;
            file.write_all(self.to_json().as_bytes())?;
            file.write_all(b"\n")?;
            file.sync_all()?;
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

/// Where the state file lives: `$STATE_DIRECTORY` (set by systemd for `StateDirectory=`; its first
/// entry if several), else the packaged default.
pub fn default_state_path(state_directory: Option<&std::ffi::OsStr>) -> PathBuf {
    let directory = state_directory
        .and_then(|value| value.to_str())
        .and_then(|value| value.split(':').next())
        .filter(|value| !value.is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("/var/lib/hsm-linux-probe"));
    directory.join(STATE_FILE_NAME)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "hsm-probe-docker-state-{tag}-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn sample() -> State {
        let key = ServiceKey::new("gitea", "db");
        let mut record = ServiceRecord::new(&key);
        record.containers.insert("abbd59dcacc8".into(), 2);
        record.restart_total = 5;
        record.restart_posted = Some(5);
        record.oom_latch_until = Some(1_790_500_000);
        record.last_seen = 1_790_413_000;
        let mut state = State::default();
        state.services.insert(key, record);
        state
    }

    #[test]
    fn the_state_file_round_trips() {
        let dir = temp_dir("roundtrip");
        let path = dir.join(STATE_FILE_NAME);
        let state = sample();
        state.save(&path).expect("save");
        let (loaded, outcome) = State::load(&path);
        assert!(matches!(outcome, LoadOutcome::Loaded(1)));
        assert_eq!(loaded, state);
        assert!(!temp_path(&path).exists(), "the temp file is renamed away");
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_missing_file_is_a_fresh_start() {
        let dir = temp_dir("missing");
        let (state, outcome) = State::load(&dir.join(STATE_FILE_NAME));
        assert!(matches!(outcome, LoadOutcome::Missing));
        assert!(state.services.is_empty());
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_corrupt_or_foreign_file_is_discarded_not_fatal() {
        let dir = temp_dir("corrupt");
        let path = dir.join(STATE_FILE_NAME);
        for text in [
            "{ truncated",
            "",
            r#"{"version": 99, "services": []}"#,
            r#"{"services": []}"#,
            r#"[1, 2, 3]"#,
        ] {
            fs::write(&path, text).unwrap();
            let (state, outcome) = State::load(&path);
            assert!(matches!(outcome, LoadOutcome::Discarded(_)), "{text}");
            assert!(state.services.is_empty());
        }
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_save_replaces_the_previous_file_whole() {
        let dir = temp_dir("replace");
        let path = dir.join(STATE_FILE_NAME);
        fs::write(&path, "garbage that must not survive").unwrap();
        sample().save(&path).expect("save");
        assert_eq!(State::load(&path).0, sample());
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_state_path_follows_systemd() {
        assert_eq!(
            default_state_path(None),
            PathBuf::from("/var/lib/hsm-linux-probe/docker-state.json")
        );
        assert_eq!(
            default_state_path(Some(std::ffi::OsStr::new("/var/lib/x:/var/lib/y"))),
            PathBuf::from("/var/lib/x/docker-state.json")
        );
    }
}
