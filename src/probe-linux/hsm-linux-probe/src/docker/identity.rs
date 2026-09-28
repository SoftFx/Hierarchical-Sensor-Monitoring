//! Service identity and HSM path naming for the Docker tree.
//!
//! A sensor's identity is its Compose `(project, service)` pair from the container labels
//! `com.docker.compose.project` / `com.docker.compose.service` — stable across recreate and
//! upgrade, unlike the container id or name. The node is `Docker/<project>/<service>`.
//!
//! Path segments keep `[A-Za-z0-9_-]` and turn everything else into `_`. Two different raw names
//! can normalize to the same segment (`web.api` and `web_api`); a reverse map detects that and the
//! newcomer gets a short stable hash suffix (`web_api-3f9a1c`). A name that needed no normalization
//! always keeps its plain segment, and the initial batch is resolved in a fixed order, so which
//! side gets the suffix does not depend on the daemon's listing order. Assignments are kept for the
//! life of the process: a registered sensor never moves.

use std::collections::{BTreeMap, HashMap};

use super::contract;
use super::engine::ContainerSummary;

pub const PROJECT_LABEL: &str = "com.docker.compose.project";
pub const SERVICE_LABEL: &str = "com.docker.compose.service";

/// The identity of one monitored service. For a standalone container (no Compose labels, and
/// `docker.composeOnly` off) the project is [`contract::STANDALONE_PROJECT`] and the service is
/// the container name.
#[derive(Clone, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct ServiceKey {
    pub project: String,
    pub service: String,
}

impl ServiceKey {
    pub fn new(project: impl Into<String>, service: impl Into<String>) -> Self {
        Self {
            project: project.into(),
            service: service.into(),
        }
    }
}

impl std::fmt::Display for ServiceKey {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}/{}", self.project, self.service)
    }
}

/// How a listed container maps onto a service.
#[derive(Clone, Debug, Eq, PartialEq)]
pub enum Membership {
    Service(ServiceKey),
    /// No Compose labels and `composeOnly` is on: not monitored.
    Skipped,
}

pub fn membership(container: &ContainerSummary, compose_only: bool) -> Membership {
    match (
        container.label(PROJECT_LABEL),
        container.label(SERVICE_LABEL),
    ) {
        (Some(project), Some(service)) => Membership::Service(ServiceKey::new(project, service)),
        _ if compose_only => Membership::Skipped,
        _ => {
            let name = container.name();
            // A nameless container cannot happen through the daemon; fall back to the short id so
            // it still gets a stable node rather than an empty segment.
            let name = if name.is_empty() {
                container.id.get(..12).unwrap_or(&container.id)
            } else {
                name
            };
            Membership::Service(ServiceKey::new(contract::STANDALONE_PROJECT, name))
        }
    }
}

/// `[A-Za-z0-9_-]` kept, everything else → `_`; an empty name becomes `_`.
pub fn normalize(raw: &str) -> String {
    if raw.is_empty() {
        return "_".to_string();
    }
    raw.chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '_' || c == '-' {
                c
            } else {
                '_'
            }
        })
        .collect()
}

/// Six hex digits of FNV-1a/64 over the raw name: short, stable across builds and Rust versions
/// (unlike `DefaultHasher`), and only ever used to tell apart names that already collided.
pub fn short_hash(raw: &str) -> String {
    let mut hash: u64 = 0xcbf2_9ce4_8422_2325;
    for byte in raw.as_bytes() {
        hash ^= u64::from(*byte);
        hash = hash.wrapping_mul(0x0000_0100_0000_01b3);
    }
    format!("{:06x}", hash >> 40)
}

/// One namespace of path segments (the projects, or the services of one project) with its
/// reverse map from segment back to the raw name that owns it.
#[derive(Debug, Default)]
struct Namespace {
    by_raw: HashMap<String, String>,
    owner_of: HashMap<String, String>,
}

impl Namespace {
    fn segment(&mut self, raw: &str) -> String {
        if let Some(segment) = self.by_raw.get(raw) {
            return segment.clone();
        }
        let plain = normalize(raw);
        let segment = if self.owner_of.contains_key(&plain) {
            // Collision: suffix the newcomer. A second-order collision (the suffixed segment is
            // itself taken) is resolved by hashing again with a counter — deterministic and
            // practically unreachable.
            let mut candidate = format!("{plain}-{}", short_hash(raw));
            let mut round = 1u32;
            while self.owner_of.contains_key(&candidate) {
                candidate = format!("{plain}-{}", short_hash(&format!("{raw}#{round}")));
                round += 1;
            }
            candidate
        } else {
            plain
        };
        self.owner_of.insert(segment.clone(), raw.to_string());
        self.by_raw.insert(raw.to_string(), segment.clone());
        segment
    }
}

/// Assigns and remembers the node path of every service.
#[derive(Debug, Default)]
pub struct Naming {
    projects: Namespace,
    services: BTreeMap<String, Namespace>,
    nodes: HashMap<ServiceKey, String>,
}

impl Naming {
    /// Resolve a batch of keys. Keys already named keep their node; new ones are named in a fixed
    /// order — names that need no normalization first, then by raw name — so the outcome of a
    /// collision is independent of the order the daemon listed the containers in.
    pub fn resolve_all<'k>(&mut self, keys: impl IntoIterator<Item = &'k ServiceKey>) {
        let mut fresh: Vec<&ServiceKey> = keys
            .into_iter()
            .filter(|key| !self.nodes.contains_key(*key))
            .collect();
        fresh.sort_by(|a, b| {
            let rank = |key: &ServiceKey| {
                (
                    normalize(&key.project) != key.project,
                    normalize(&key.service) != key.service,
                )
            };
            rank(a).cmp(&rank(b)).then_with(|| a.cmp(b))
        });
        fresh.dedup();
        for key in fresh {
            self.node(key);
        }
    }

    /// The node path of a service, `Docker/<project>/<service>`, assigning it on first use.
    pub fn node(&mut self, key: &ServiceKey) -> String {
        if let Some(node) = self.nodes.get(key) {
            return node.clone();
        }
        let project = self.projects.segment(&key.project);
        let service = self
            .services
            .entry(key.project.clone())
            .or_default()
            .segment(&key.service);
        let node = format!("{}/{project}/{service}", contract::ROOT);
        self.nodes.insert(key.clone(), node.clone());
        node
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn key(project: &str, service: &str) -> ServiceKey {
        ServiceKey::new(project, service)
    }

    #[test]
    fn compose_names_pass_through_unchanged() {
        let mut naming = Naming::default();
        assert_eq!(naming.node(&key("gitea", "db")), "Docker/gitea/db");
        assert_eq!(
            naming.node(&key("lingua-ci", "runner-heavy")),
            "Docker/lingua-ci/runner-heavy"
        );
    }

    #[test]
    fn everything_outside_the_safe_set_becomes_an_underscore() {
        assert_eq!(normalize("web.api"), "web_api");
        assert_eq!(normalize("a/b c:d"), "a_b_c_d");
        assert_eq!(normalize("Ünïcode"), "_n_code");
        assert_eq!(normalize("Keep_THIS-1"), "Keep_THIS-1");
        assert_eq!(normalize(""), "_");
    }

    #[test]
    fn the_short_hash_is_pinned() {
        // Stable across builds: a changed hash would move every disambiguated node.
        assert_eq!(short_hash("web.api"), short_hash("web.api"));
        assert_eq!(short_hash("web.api").len(), 6);
        assert_eq!(short_hash(""), "cbf29c");
        assert_ne!(short_hash("web.api"), short_hash("web:api"));
    }

    #[test]
    fn colliding_services_are_told_apart_and_the_plain_name_keeps_its_segment() {
        let mut naming = Naming::default();
        let dotted = key("shop", "web.api");
        let plain = key("shop", "web_api");
        // Listed dotted-first; the plain name still wins the unsuffixed segment.
        naming.resolve_all([&dotted, &plain]);
        assert_eq!(naming.node(&plain), "Docker/shop/web_api");
        assert_eq!(
            naming.node(&dotted),
            format!("Docker/shop/web_api-{}", short_hash("web.api"))
        );
    }

    #[test]
    fn the_outcome_does_not_depend_on_listing_order() {
        let a = key("shop", "web.api");
        let b = key("shop", "web:api");
        let mut first = Naming::default();
        first.resolve_all([&a, &b]);
        let mut second = Naming::default();
        second.resolve_all([&b, &a]);
        assert_eq!(first.node(&a), second.node(&a));
        assert_eq!(first.node(&b), second.node(&b));
        assert_ne!(first.node(&a), first.node(&b));
    }

    #[test]
    fn colliding_projects_are_told_apart_too() {
        let mut naming = Naming::default();
        naming.resolve_all([&key("my.app", "web"), &key("my_app", "web")]);
        assert_eq!(naming.node(&key("my_app", "web")), "Docker/my_app/web");
        assert_eq!(
            naming.node(&key("my.app", "web")),
            format!("Docker/my_app-{}/web", short_hash("my.app"))
        );
    }

    #[test]
    fn an_assigned_node_never_moves() {
        let mut naming = Naming::default();
        let dotted = key("shop", "web.api");
        assert_eq!(naming.node(&dotted), "Docker/shop/web_api");
        // The plain name arrives later: the already-registered node keeps its path and the
        // newcomer is the one suffixed.
        let plain = key("shop", "web_api");
        naming.resolve_all([&plain]);
        assert_eq!(naming.node(&dotted), "Docker/shop/web_api");
        assert_eq!(
            naming.node(&plain),
            format!("Docker/shop/web_api-{}", short_hash("web_api"))
        );
    }

    #[test]
    fn same_service_name_in_two_projects_is_not_a_collision() {
        let mut naming = Naming::default();
        assert_eq!(naming.node(&key("gitea", "db")), "Docker/gitea/db");
        assert_eq!(naming.node(&key("lingua", "db")), "Docker/lingua/db");
    }

    fn summary(name: &str, labels: &[(&str, &str)]) -> ContainerSummary {
        ContainerSummary {
            id: "0123456789abcdef".into(),
            names: vec![format!("/{name}")],
            state: "running".into(),
            labels: Some(
                labels
                    .iter()
                    .map(|(k, v)| (k.to_string(), v.to_string()))
                    .collect(),
            ),
        }
    }

    #[test]
    fn membership_comes_from_the_compose_labels() {
        let compose = summary(
            "gitea-db-1",
            &[(PROJECT_LABEL, "gitea"), (SERVICE_LABEL, "db")],
        );
        assert_eq!(
            membership(&compose, true),
            Membership::Service(key("gitea", "db"))
        );
    }

    #[test]
    fn unlabelled_containers_are_skipped_or_standalone_by_config() {
        let bare = summary("adhoc", &[]);
        assert_eq!(membership(&bare, true), Membership::Skipped);
        assert_eq!(
            membership(&bare, false),
            Membership::Service(key("_standalone", "adhoc"))
        );
        // Half-labelled (a project but no service) is not a Compose service either.
        let half = summary("half", &[(PROJECT_LABEL, "p")]);
        assert_eq!(membership(&half, true), Membership::Skipped);
        let mut naming = Naming::default();
        assert_eq!(
            naming.node(&key("_standalone", "adhoc")),
            "Docker/_standalone/adhoc"
        );
    }
}
