//! Docker Engine API client: the four read-only endpoints of initiative §4.3 and nothing else.
//!
//! Read-only by construction: the only requests this module can build are the variants of
//! [`Endpoint`], all `GET`s, and [`super::http`] has no other method. The socket is
//! root-equivalent (anyone who can write to it can start a privileged container), so this is the
//! whole of the probe's use of it — enforced by review of this file, not by the kernel.

use std::collections::HashMap;
use std::fmt;
use std::path::PathBuf;

use serde::Deserialize;

use super::contract;
use super::http::{self, HttpError};

/// Every request the probe can make.
#[derive(Clone, Debug, Eq, PartialEq)]
pub enum Endpoint {
    /// `GET /version` — unversioned, so it works before negotiation.
    Version,
    /// `GET /v<api>/containers/json?all=true`
    Containers,
    /// `GET /v<api>/containers/{id}/json`
    Inspect(String),
    /// `GET /v<api>/containers/{id}/stats?stream=false&one-shot=true`
    Stats(String),
}

impl Endpoint {
    /// The request target. Container ids come from the daemon's own list; anything that is not a
    /// plain hex id is refused rather than spliced into a path.
    pub fn path(&self, api: ApiVersion) -> Result<String, EngineError> {
        Ok(match self {
            Endpoint::Version => "/version".to_string(),
            Endpoint::Containers => format!("/v{api}/containers/json?all=true"),
            Endpoint::Inspect(id) => format!("/v{api}/containers/{}/json", checked_id(id)?),
            Endpoint::Stats(id) => format!(
                "/v{api}/containers/{}/stats?stream=false&one-shot=true",
                checked_id(id)?
            ),
        })
    }
}

fn checked_id(id: &str) -> Result<&str, EngineError> {
    if !id.is_empty() && id.len() <= 64 && id.bytes().all(|b| b.is_ascii_hexdigit()) {
        Ok(id)
    } else {
        Err(EngineError::Invalid("container id is not a hex id".into()))
    }
}

/// An Engine API version, `major.minor`.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct ApiVersion(pub u32, pub u32);

impl ApiVersion {
    pub const PINNED: ApiVersion = ApiVersion(
        contract::PINNED_API_VERSION.0,
        contract::PINNED_API_VERSION.1,
    );
    pub const OLDEST: ApiVersion = ApiVersion(
        contract::OLDEST_API_VERSION.0,
        contract::OLDEST_API_VERSION.1,
    );

    pub fn parse(text: &str) -> Option<Self> {
        let (major, minor) = text.trim().split_once('.')?;
        Some(ApiVersion(major.parse().ok()?, minor.parse().ok()?))
    }
}

impl fmt::Display for ApiVersion {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}.{}", self.0, self.1)
    }
}

/// Pick the version to pin in request paths.
///
/// The probe speaks [`ApiVersion::PINNED`] (the version its fixtures come from). A daemon that no
/// longer accepts it (`MinAPIVersion` above it) is spoken to at its minimum — the fields read here
/// have been stable since long before 1.41; a daemon older than the pin is spoken to at its own
/// version as long as it has one-shot stats ([`ApiVersion::OLDEST`]). Anything older is refused.
pub fn negotiate_api_version(
    daemon_min: ApiVersion,
    daemon_max: ApiVersion,
) -> Result<ApiVersion, EngineError> {
    let chosen = ApiVersion::PINNED.clamp(daemon_min.min(daemon_max), daemon_max);
    if chosen < ApiVersion::OLDEST {
        return Err(EngineError::Invalid(format!(
            "the daemon speaks Engine API {daemon_max}; the probe needs at least {}",
            ApiVersion::OLDEST
        )));
    }
    Ok(chosen)
}

#[derive(Debug)]
pub enum EngineError {
    /// The socket is missing, refused, not permitted, or timed out: the daemon is unavailable.
    Unavailable(HttpError),
    /// The daemon answered with a non-2xx status.
    Status { status: u16, message: String },
    /// The response did not parse, or a request could not be built.
    Invalid(String),
}

impl EngineError {
    /// Whether this failure means the daemon as a whole is unreachable (back off), as opposed to
    /// one request failing (skip that value).
    pub fn is_unavailable(&self) -> bool {
        matches!(self, EngineError::Unavailable(_))
    }

    /// Whether the socket refused this process for lack of permission — the one failure an
    /// operator fixes in the unit, so it gets its own hint.
    pub fn is_permission_denied(&self) -> bool {
        matches!(self, EngineError::Unavailable(HttpError::Io(error))
            if error.kind() == std::io::ErrorKind::PermissionDenied)
    }

    /// Whether one request ran past its deadline. For a per-container call that is a failed read
    /// of that container (it may be the one wedged), not proof the whole daemon is gone.
    pub fn is_timeout(&self) -> bool {
        matches!(self, EngineError::Unavailable(HttpError::Timeout))
    }

    /// Whether there is no socket at all — a host without Docker, which is not an error.
    pub fn is_socket_missing(&self) -> bool {
        matches!(self, EngineError::Unavailable(HttpError::Io(error))
            if error.kind() == std::io::ErrorKind::NotFound)
    }
}

impl fmt::Display for EngineError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            EngineError::Unavailable(error) => write!(f, "Docker Engine API unavailable: {error}"),
            EngineError::Status { status, message } => {
                write!(f, "Docker Engine API answered {status}: {message}")
            }
            EngineError::Invalid(message) => write!(f, "Docker Engine API: {message}"),
        }
    }
}

impl std::error::Error for EngineError {}

// ---- Response DTOs: only the fields the probe reads; everything else is ignored. --------------

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct VersionInfo {
    #[serde(default)]
    pub version: String,
    #[serde(default)]
    pub api_version: String,
    #[serde(default, rename = "MinAPIVersion")]
    pub min_api_version: String,
}

/// One row of `/containers/json?all=true`.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ContainerSummary {
    pub id: String,
    #[serde(default)]
    pub names: Vec<String>,
    /// created | restarting | running | removing | paused | exited | dead
    #[serde(default)]
    pub state: String,
    #[serde(default)]
    pub labels: Option<HashMap<String, String>>,
}

impl ContainerSummary {
    pub fn label(&self, name: &str) -> Option<&str> {
        self.labels
            .as_ref()?
            .get(name)
            .map(String::as_str)
            .filter(|value| !value.is_empty())
    }

    /// The container name without Docker's leading `/`.
    pub fn name(&self) -> &str {
        self.names
            .first()
            .map(|name| name.trim_start_matches('/'))
            .unwrap_or_default()
    }
}

/// The part of `/containers/{id}/json` the probe reads.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ContainerInspect {
    #[serde(default)]
    pub restart_count: u64,
    #[serde(default)]
    pub state: InspectState,
    #[serde(default)]
    pub host_config: InspectHostConfig,
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct InspectHostConfig {
    #[serde(default)]
    pub restart_policy: InspectRestartPolicy,
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct InspectRestartPolicy {
    /// `no` (or empty), `always`, `unless-stopped`, `on-failure`.
    #[serde(default)]
    pub name: String,
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct InspectState {
    #[serde(default, rename = "OOMKilled")]
    pub oom_killed: bool,
    #[serde(default)]
    pub exit_code: i64,
    /// Present only when the image or compose file defines a healthcheck.
    #[serde(default)]
    pub health: Option<InspectHealth>,
}

#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct InspectHealth {
    #[serde(default)]
    pub status: String,
}

/// The part of a one-shot `/containers/{id}/stats` the probe reads. A stopped container answers
/// with zeros and empty objects, so every counter is optional and absence means "no sample".
#[derive(Clone, Debug, Default, Deserialize)]
pub struct ContainerStats {
    #[serde(default)]
    pub cpu_stats: CpuStats,
    #[serde(default)]
    pub memory_stats: MemoryStats,
}

#[derive(Clone, Debug, Default, Deserialize)]
pub struct CpuStats {
    #[serde(default)]
    pub cpu_usage: CpuUsage,
    /// Host CPU time across all cores, ns. Absent for a container that is not running.
    #[serde(default)]
    pub system_cpu_usage: Option<u64>,
}

#[derive(Clone, Debug, Default, Deserialize)]
pub struct CpuUsage {
    /// The container's cumulative CPU time, ns.
    #[serde(default)]
    pub total_usage: Option<u64>,
}

#[derive(Clone, Debug, Default, Deserialize)]
pub struct MemoryStats {
    #[serde(default)]
    pub usage: Option<u64>,
    #[serde(default)]
    pub limit: Option<u64>,
    /// cgroup v2 carries `inactive_file`; cgroup v1 `total_inactive_file`.
    #[serde(default)]
    pub stats: Option<HashMap<String, u64>>,
}

/// What the Docker source needs from the daemon. The production implementation is [`Engine`];
/// tests feed captured responses through the same trait.
pub trait EngineApi {
    /// Pin the negotiated API version for every later request.
    fn set_api_version(&mut self, _api: ApiVersion) {}
    fn version(&mut self) -> Result<VersionInfo, EngineError>;
    fn containers(&mut self) -> Result<Vec<ContainerSummary>, EngineError>;
    fn inspect(&mut self, id: &str) -> Result<ContainerInspect, EngineError>;
    fn stats(&mut self, id: &str) -> Result<ContainerStats, EngineError>;
}

/// The Engine API over the daemon's Unix socket.
pub struct Engine {
    socket: PathBuf,
    api: ApiVersion,
}

impl Engine {
    pub fn new(socket: PathBuf) -> Self {
        Self {
            socket,
            api: ApiVersion::PINNED,
        }
    }

    fn get<T: for<'de> Deserialize<'de>>(&self, endpoint: Endpoint) -> Result<T, EngineError> {
        let path = endpoint.path(self.api)?;
        let response = http::get(
            &self.socket,
            &path,
            contract::REQUEST_TIMEOUT,
            contract::MAX_RESPONSE_BYTES,
        )
        .map_err(|error| match error {
            // A malformed or oversized answer is a broken response, not an unreachable daemon.
            HttpError::Malformed(_) | HttpError::TooLarge => {
                EngineError::Invalid(error.to_string())
            }
            other => EngineError::Unavailable(other),
        })?;
        decode(response.status, &response.body)
    }
}

/// Map a status + body to a typed value or an error carrying the daemon's own message.
pub fn decode<T: for<'de> Deserialize<'de>>(status: u16, body: &[u8]) -> Result<T, EngineError> {
    if !(200..300).contains(&status) {
        #[derive(Deserialize)]
        struct Message {
            message: String,
        }
        let message = serde_json::from_slice::<Message>(body)
            .map(|m| m.message)
            .unwrap_or_default();
        // The daemon's message names the container; keep log lines bounded all the same.
        let message: String = message.chars().take(200).collect();
        return Err(EngineError::Status { status, message });
    }
    serde_json::from_slice(body).map_err(|error| EngineError::Invalid(error.to_string()))
}

impl EngineApi for Engine {
    fn set_api_version(&mut self, api: ApiVersion) {
        self.api = api;
    }

    fn version(&mut self) -> Result<VersionInfo, EngineError> {
        self.get(Endpoint::Version)
    }

    fn containers(&mut self) -> Result<Vec<ContainerSummary>, EngineError> {
        self.get(Endpoint::Containers)
    }

    fn inspect(&mut self, id: &str) -> Result<ContainerInspect, EngineError> {
        self.get(Endpoint::Inspect(id.to_string()))
    }

    fn stats(&mut self, id: &str) -> Result<ContainerStats, EngineError> {
        self.get(Endpoint::Stats(id.to_string()))
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    pub const VERSION: &str = include_str!("../../../fixtures/docker/garage/version.json");
    pub const CONTAINERS: &str = include_str!("../../../fixtures/docker/garage/containers.json");
    pub const INSPECT: &str = include_str!("../../../fixtures/docker/garage/inspect.json");
    pub const STATS_T0: &str = include_str!("../../../fixtures/docker/garage/stats-t0.json");
    pub const STATS_T1: &str = include_str!("../../../fixtures/docker/garage/stats-t1.json");

    #[test]
    fn only_read_only_endpoints_can_be_built() {
        let api = ApiVersion(1, 45);
        assert_eq!(Endpoint::Version.path(api).unwrap(), "/version");
        assert_eq!(
            Endpoint::Containers.path(api).unwrap(),
            "/v1.45/containers/json?all=true"
        );
        assert_eq!(
            Endpoint::Inspect("abbd59dcacc8".into()).path(api).unwrap(),
            "/v1.45/containers/abbd59dcacc8/json"
        );
        assert_eq!(
            Endpoint::Stats("abbd59dcacc8".into()).path(api).unwrap(),
            "/v1.45/containers/abbd59dcacc8/stats?stream=false&one-shot=true"
        );
    }

    #[test]
    fn a_container_id_cannot_smuggle_another_path() {
        for id in [
            "",
            "../../containers/create",
            "abc/kill",
            "abc?signal=9",
            "abc def",
        ] {
            assert!(
                Endpoint::Inspect(id.into())
                    .path(ApiVersion::PINNED)
                    .is_err(),
                "{id}"
            );
            assert!(Endpoint::Stats(id.into()).path(ApiVersion::PINNED).is_err());
        }
    }

    #[test]
    fn the_api_version_is_negotiated_around_the_pin() {
        let v = |a, b| ApiVersion(a, b);
        // garage: Docker 26.1 speaks 1.24..=1.45 -> the pin.
        assert_eq!(negotiate_api_version(v(1, 24), v(1, 45)).unwrap(), v(1, 45));
        // Docker Desktop 29: 1.40..=1.55 -> still the pin.
        assert_eq!(negotiate_api_version(v(1, 40), v(1, 55)).unwrap(), v(1, 45));
        // A daemon that dropped 1.45 -> its minimum.
        assert_eq!(negotiate_api_version(v(1, 47), v(1, 60)).unwrap(), v(1, 47));
        // An older daemon with one-shot stats -> its own version.
        assert_eq!(negotiate_api_version(v(1, 24), v(1, 43)).unwrap(), v(1, 43));
        // Too old for one-shot stats -> refused.
        assert!(negotiate_api_version(v(1, 12), v(1, 40)).is_err());
    }

    #[test]
    fn the_garage_version_parses() {
        let version: VersionInfo = serde_json::from_str(VERSION).unwrap();
        assert_eq!(version.version, "26.1.5+dfsg1");
        assert_eq!(
            ApiVersion::parse(&version.api_version),
            Some(ApiVersion(1, 45))
        );
        assert_eq!(
            ApiVersion::parse(&version.min_api_version),
            Some(ApiVersion(1, 24))
        );
    }

    #[test]
    fn the_garage_container_list_parses() {
        let list: Vec<ContainerSummary> = serde_json::from_str(CONTAINERS).unwrap();
        assert_eq!(list.len(), 12);
        let db = list.iter().find(|c| c.name() == "gitea-db-1").unwrap();
        assert_eq!(db.state, "running");
        assert_eq!(db.label("com.docker.compose.project"), Some("gitea"));
        assert_eq!(db.label("com.docker.compose.service"), Some("db"));
        let ci = list
            .iter()
            .find(|c| c.name() == "lingua-ci-ci-image-1")
            .unwrap();
        assert_eq!(ci.state, "exited");
    }

    #[test]
    fn the_garage_inspects_parse() {
        let inspects: Vec<ContainerInspect> = serde_json::from_str(INSPECT).unwrap();
        assert_eq!(inspects.len(), 12);
        let with_health: Vec<_> = inspects
            .iter()
            .filter(|i| i.state.health.is_some())
            .collect();
        assert_eq!(with_health.len(), 4, "seaweedfs, mongo, gitea, db");
        assert!(with_health
            .iter()
            .all(|i| i.state.health.as_ref().unwrap().status == "healthy"));
        assert!(inspects.iter().all(|i| !i.state.oom_killed));
    }

    #[test]
    fn stats_of_a_stopped_container_carry_no_counters() {
        let stats: HashMap<String, ContainerStats> = serde_json::from_str(STATS_T0).unwrap();
        let stopped = stats
            .iter()
            .find(|(id, _)| id.starts_with("3537ef2ee867"))
            .unwrap()
            .1;
        assert_eq!(stopped.cpu_stats.system_cpu_usage, None);
        assert_eq!(stopped.memory_stats.usage, None);
    }

    #[test]
    fn a_daemon_error_keeps_its_status_and_message() {
        let error =
            decode::<ContainerInspect>(404, br#"{"message":"No such container: x"}"#).unwrap_err();
        assert!(matches!(
            error,
            EngineError::Status { status: 404, ref message } if message == "No such container: x"
        ));
        assert!(!error.is_unavailable());
    }

    #[test]
    fn a_body_that_is_not_the_expected_json_is_invalid() {
        assert!(matches!(
            decode::<Vec<ContainerSummary>>(200, b"{\"not\":\"a list\"}"),
            Err(EngineError::Invalid(_))
        ));
    }
}
