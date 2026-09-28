//! The Docker Compose source (#1416): per-service CPU, memory, status, health, restarts and OOM
//! kills under `<module>/Docker/<project>/<service>/…`, read from the Docker Engine API over its
//! Unix socket.
//!
//! One thread, two cadences: stats every `docker.samplePeriodSec` (CPU and memory samples into
//! 5-minute bars) and a 60-second state poll (listing + inspect: status, health, restart count,
//! OOM). The contract — paths, types, cadences, thresholds — is [`contract`]; the per-service
//! state machines are [`tracker`]; naming is [`identity`]; math is [`stats`]; the durable memory is
//! [`state`].
//!
//! Isolation (root rules #6/#8): the source never takes the probe down and never invents a value.
//! A panic inside a tick is caught and logged; an unreachable daemon is logged once, retried with
//! a bounded backoff and resumed silently; a failed read skips that value (logged, deduplicated)
//! rather than posting 0, `false` or a stale state.

pub mod alerts;
pub mod contract;
pub mod engine;
pub mod http;
pub mod identity;
pub mod sensors;
pub mod state;
pub mod stats;
pub mod tracker;

use std::collections::{BTreeMap, HashSet};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use hsm_collector::Collector;

use crate::config::DockerConfig;
use crate::logging::{Level, Logger};

use engine::{ApiVersion, EngineApi, EngineError};
use identity::{Membership, Naming, ServiceKey};
use sensors::ServiceSensors;
use state::{LoadOutcome, State};
use stats::{CpuCounters, CpuTracker};
use tracker::{ContainerObservation, InspectFacts, Tracker};

pub use engine::Engine;

/// How often the source thread wakes to check the stop flag while idle.
const TICK: Duration = Duration::from_millis(200);
/// Bound on the log-once key set (the managed `MessageDeduplicator` lesson: diverse keys must not
/// grow memory without limit). Past it the set is cleared, which at worst repeats a line.
const LOG_ONCE_CAPACITY: usize = 1024;

/// Log a condition once until it clears, instead of once per tick.
#[derive(Debug, Default)]
struct LogOnce {
    active: HashSet<String>,
}

impl LogOnce {
    /// True the first time `key` is raised (log it now).
    fn raise(&mut self, key: &str) -> bool {
        if self.active.contains(key) {
            return false;
        }
        if self.active.len() >= LOG_ONCE_CAPACITY {
            self.active.clear();
        }
        self.active.insert(key.to_string());
        true
    }

    /// True when `key` was raised and is now cleared (the condition recovered).
    fn clear(&mut self, key: &str) -> bool {
        self.active.remove(key)
    }
}

/// The Docker source. Owns its sensors; borrows the collector for their lifetime.
pub struct DockerSource<'c, E: EngineApi> {
    collector: &'c Collector,
    engine: E,
    logger: Arc<Logger>,
    sample_period: Duration,
    oom_latch: Duration,
    compose_only: bool,
    state_path: Option<PathBuf>,
    host_mem_total: Option<u64>,
    api: Option<ApiVersion>,
    naming: Naming,
    tracker: Tracker,
    sensors: BTreeMap<ServiceKey, ServiceSensors<'c>>,
    /// Containers to sample, per service: those the last poll saw running or paused.
    roster: BTreeMap<ServiceKey, Vec<String>>,
    cpu: CpuTracker,
    log_once: LogOnce,
    origin: Instant,
}

impl<'c, E: EngineApi> DockerSource<'c, E> {
    /// Build the source and load its state. `state_path = None` keeps the state in memory only.
    pub fn new(
        collector: &'c Collector,
        engine: E,
        config: &DockerConfig,
        logger: Arc<Logger>,
        state_path: Option<PathBuf>,
        host_mem_total: Option<u64>,
    ) -> Self {
        let state = match &state_path {
            None => State::default(),
            Some(path) => {
                let (state, outcome) = State::load(path);
                match outcome {
                    LoadOutcome::Loaded(count) => logger.info(format!(
                        "docker: state loaded from {} ({count} service(s) remembered)",
                        path.display()
                    )),
                    LoadOutcome::Missing => logger.info(format!(
                        "docker: no state file at {} yet; starting fresh",
                        path.display()
                    )),
                    LoadOutcome::Discarded(reason) => logger.warn(format!(
                        "docker: state file {} discarded ({reason}); starting fresh — restart \
                         baselines and OOM latches are reset",
                        path.display()
                    )),
                }
                state
            }
        };
        if host_mem_total.is_none() {
            logger.warn(
                "docker: host MemTotal unknown; a container without a memory limit is measured \
                 against the limit Docker reports",
            );
        }
        Self {
            collector,
            engine,
            logger,
            sample_period: config.sample_period(),
            oom_latch: config.oom_latch(),
            compose_only: config.compose_only,
            state_path,
            host_mem_total,
            api: None,
            naming: Naming::default(),
            tracker: Tracker::new(state),
            sensors: BTreeMap::new(),
            roster: BTreeMap::new(),
            cpu: CpuTracker::default(),
            log_once: LogOnce::default(),
            origin: Instant::now(),
        }
    }

    /// Run until `should_stop` returns true. Never panics out: a panicking tick is logged and the
    /// loop goes on.
    pub fn run(&mut self, should_stop: &dyn Fn() -> bool) {
        let mut next_poll = Instant::now();
        let mut next_sample = next_poll + self.sample_period;
        let mut backoff = self.sample_period;
        let mut unavailable_until: Option<Instant> = None;

        while !should_stop() {
            let now = Instant::now();
            if unavailable_until.is_some_and(|until| now < until) {
                std::thread::sleep(TICK);
                continue;
            }

            let mut failure = None;
            if now >= next_poll {
                failure = self.guarded("poll", |source| source.poll(should_stop));
                next_poll = advance(next_poll, contract::STATE_POLL_PERIOD, now);
            }
            if failure.is_none() && now >= next_sample {
                failure = self.guarded("sample", |source| source.sample(should_stop));
                next_sample = advance(next_sample, self.sample_period, now);
            }

            match failure {
                Some(error) => {
                    if self.log_once.raise("engine") {
                        if error.is_socket_missing() {
                            // A host without Docker: nothing is wrong, nothing to report.
                            self.logger.info(format!(
                                "docker: {error}; no Docker on this host? The Docker source waits \
                                 for the socket (set docker.enabled = false to silence this)"
                            ));
                        } else {
                            let hint = if error.is_permission_denied() {
                                " — the probe needs the docker group: see docker-access.sh / \
                                 the docker.conf drop-in in the README"
                            } else {
                                ""
                            };
                            self.logger.error(format!(
                                "docker: {error}{hint}; Docker values are skipped until it \
                                 answers (retrying with backoff up to {} s)",
                                contract::MAX_BACKOFF.as_secs()
                            ));
                        }
                    }
                    unavailable_until = Some(Instant::now() + backoff);
                    backoff = (backoff * 2).min(contract::MAX_BACKOFF);
                    // Re-list first thing after the outage.
                    next_poll = Instant::now();
                }
                None => {
                    unavailable_until = None;
                    backoff = self.sample_period;
                    if self.log_once.clear("engine") {
                        self.logger
                            .log(Level::Debug, "docker: the Engine API answers again");
                    }
                }
            }

            let wake = next_poll.min(next_sample);
            while !should_stop() && Instant::now() < wake {
                std::thread::sleep(TICK.min(wake.saturating_duration_since(Instant::now())));
            }
        }

        self.persist();
    }

    /// Run one tick, containing a panic. Returns the error that should trigger backoff, if any.
    fn guarded(
        &mut self,
        what: &str,
        tick: impl FnOnce(&mut Self) -> Result<(), EngineError>,
    ) -> Option<EngineError> {
        match catch_unwind(AssertUnwindSafe(|| tick(self))) {
            Ok(Ok(())) => None,
            Ok(Err(error)) => Some(error),
            Err(panic) => {
                let message = panic
                    .downcast_ref::<&str>()
                    .map(|s| (*s).to_string())
                    .or_else(|| panic.downcast_ref::<String>().cloned())
                    .unwrap_or_else(|| "unknown panic".to_string());
                if self.log_once.raise(&format!("panic:{what}")) {
                    self.logger.error(format!(
                        "docker: the {what} tick panicked ({message}); skipped"
                    ));
                }
                None
            }
        }
    }

    fn ensure_api(&mut self) -> Result<(), EngineError> {
        if self.api.is_some() {
            return Ok(());
        }
        let version = self.engine.version()?;
        let parse = |text: &str| {
            ApiVersion::parse(text)
                .ok_or_else(|| EngineError::Invalid(format!("unparsable API version '{text}'")))
        };
        let max = parse(&version.api_version)?;
        let min = if version.min_api_version.is_empty() {
            max
        } else {
            parse(&version.min_api_version)?
        };
        let api = engine::negotiate_api_version(min, max)?;
        self.engine.set_api_version(api);
        self.logger.info(format!(
            "docker: Engine {} (API {min}..={max}); speaking API {api}",
            version.version
        ));
        self.api = Some(api);
        Ok(())
    }

    /// The 60-second state poll: list, inspect, advance the state machines, post.
    pub fn poll(&mut self, should_stop: &dyn Fn() -> bool) -> Result<(), EngineError> {
        self.ensure_api()?;
        let containers = self.engine.containers()?;

        let mut services: BTreeMap<ServiceKey, Vec<ContainerObservation>> = BTreeMap::new();
        let mut roster: BTreeMap<ServiceKey, Vec<String>> = BTreeMap::new();
        for container in &containers {
            let key = match identity::membership(container, self.compose_only) {
                Membership::Service(key) => key,
                Membership::Skipped => {
                    if self.log_once.raise(&format!("skip:{}", container.id)) {
                        self.logger.info(format!(
                            "docker: container '{}' ({}) has no Compose labels; not monitored \
                             (docker.composeOnly)",
                            container.name(),
                            short_id(&container.id)
                        ));
                    }
                    continue;
                }
            };
            if should_stop() {
                return Ok(());
            }
            let inspect = match self.engine.inspect(&container.id) {
                Ok(inspect) => {
                    self.clear_read_failure("inspect", &container.id);
                    Some(InspectFacts {
                        restart_count: inspect.restart_count,
                        oom_killed: inspect.state.oom_killed,
                        health: inspect.state.health.map(|health| health.status),
                    })
                }
                Err(error) if error.is_unavailable() => return Err(error),
                Err(error) => {
                    self.read_failure("inspect", &container.id, &key, &error);
                    None
                }
            };
            if matches!(container.state.as_str(), "running" | "paused") {
                roster
                    .entry(key.clone())
                    .or_default()
                    .push(container.id.clone());
            }
            if tracker::service_status_of(&container.state).is_none()
                && self.log_once.raise(&format!("state:{}", container.state))
            {
                self.logger.warn(format!(
                    "docker: unknown container state '{}' ({key}); not reflected in Service status",
                    container.state
                ));
            }
            services.entry(key).or_default().push(ContainerObservation {
                id: container.id.clone(),
                state: container.state.clone(),
                inspect,
            });
        }

        let outcome = self.tracker.observe(&services, unix_now(), self.oom_latch);
        self.naming
            .resolve_all(services.keys().chain(outcome.reports.keys()));

        for key in &outcome.forgotten {
            self.logger.info(format!(
                "docker: forgetting {key}: not seen for more than {} days",
                contract::VANISHED_SERVICE_RETENTION.as_secs() / 86_400
            ));
            self.sensors.remove(key);
        }

        for (key, report) in &outcome.reports {
            let node = self.naming.node(key);
            let sensors = self
                .sensors
                .entry(key.clone())
                .or_insert_with(|| ServiceSensors::new(node));
            let problems = sensors.ensure_state_sensors(
                self.collector,
                report.present,
                report.has_healthcheck,
            );
            report_problems(&self.logger, &mut self.log_once, &problems);

            let mut posted_restart = None;
            let mut failures = Vec::new();
            if let (Some(sensor), Some(value)) = (&sensors.status, report.status) {
                if let Err(error) = sensor.add(value) {
                    failures.push(format!("Service status: {error}"));
                }
            }
            if let (Some(sensor), Some(value)) = (&sensors.health, report.health) {
                if let Err(error) = sensor.add(value) {
                    failures.push(format!("Health: {error}"));
                }
            }
            if let (Some(sensor), Some(value)) = (&sensors.restart_count, report.restart_count) {
                match sensor.add(i32::try_from(value).unwrap_or(i32::MAX)) {
                    Ok(()) => posted_restart = Some(value),
                    Err(error) => failures.push(format!("Restart count: {error}")),
                }
            }
            if let (Some(sensor), Some(value)) = (&sensors.oom_killed, report.oom_killed) {
                if let Err(error) = sensor.add(value) {
                    failures.push(format!("OOM killed: {error}"));
                }
            }
            let node = sensors.node.clone();
            if let Some(value) = posted_restart {
                self.tracker.restart_posted(key, value);
            }
            for failure in failures {
                if self.log_once.raise(&format!("post:{node}:{failure}")) {
                    self.logger
                        .error(format!("docker: {node}: value not posted: {failure}"));
                }
            }
        }

        self.roster = roster;
        self.cpu.retain(|id| {
            self.roster
                .values()
                .any(|ids| ids.iter().any(|listed| listed == id))
        });
        self.persist_if_dirty();
        Ok(())
    }

    /// The stats tick: one one-shot stats call per sampled container, CPU and memory into bars.
    pub fn sample(&mut self, should_stop: &dyn Fn() -> bool) -> Result<(), EngineError> {
        if self.api.is_none() {
            // No successful poll yet: nothing to sample.
            return Ok(());
        }
        let roster = self.roster.clone();
        for (key, ids) in &roster {
            let mut cpu_total = 0.0;
            let mut cpu_valid = true;
            let mut readings = Vec::with_capacity(ids.len());
            let mut memory_valid = true;
            let mut any_counters = false;

            for id in ids {
                if should_stop() {
                    return Ok(());
                }
                let stats = match self.engine.stats(id) {
                    Ok(stats) => {
                        self.clear_read_failure("stats", id);
                        stats
                    }
                    Err(error) if error.is_unavailable() => return Err(error),
                    Err(error) => {
                        self.read_failure("stats", id, key, &error);
                        self.cpu.forget(id);
                        cpu_valid = false;
                        memory_valid = false;
                        continue;
                    }
                };
                // Stamped per container, when its counters arrived: a slow call for one container
                // must not skew the interval of the next.
                match CpuCounters::from_stats(&stats, self.origin.elapsed()) {
                    Some(counters) => {
                        any_counters = true;
                        match self.cpu.sample(id, counters, self.sample_period) {
                            Ok(percent) => cpu_total += percent,
                            Err(_) => cpu_valid = false,
                        }
                    }
                    None => {
                        // Stopped between the poll and now; the next poll drops it.
                        self.cpu.forget(id);
                        cpu_valid = false;
                    }
                }
                match stats::memory_reading(&stats, self.host_mem_total) {
                    Some(reading) => readings.push(reading),
                    None => memory_valid = false,
                }
            }

            if !any_counters {
                continue;
            }
            let node = self.naming.node(key);
            let sensors = self
                .sensors
                .entry(key.clone())
                .or_insert_with(|| ServiceSensors::new(node));
            let problems = sensors.ensure_stats_sensors(self.collector);
            report_problems(&self.logger, &mut self.log_once, &problems);

            let mut failures = Vec::new();
            let memory = memory_valid
                .then(|| stats::service_memory(&readings, self.host_mem_total))
                .flatten();
            self.logger.log(
                Level::Debug,
                &format!(
                    "docker: {}: CPU {}, memory {}",
                    sensors.node,
                    if cpu_valid {
                        format!("{:.2} % of the host", cpu_total.min(100.0))
                    } else {
                        "skipped".to_string()
                    },
                    memory.map_or_else(
                        || "skipped".to_string(),
                        |m| format!(
                            "{:.2} % of {} MB{}",
                            m.used_percent,
                            stats::limit_megabytes(m.limit_bytes),
                            if m.unlimited { " (no limit: host)" } else { "" }
                        )
                    )
                ),
            );
            if cpu_valid {
                if let Some(sensor) = &sensors.cpu {
                    if let Err(error) = sensor.add(cpu_total.min(100.0)) {
                        failures.push(format!("CPU: {error}"));
                    }
                }
            }
            if let Some(memory) = memory {
                if let Some(sensor) = &sensors.memory_used {
                    if let Err(error) = sensor.add(memory.used_percent) {
                        failures.push(format!("Memory used %: {error}"));
                    }
                }
                if let Err(error) = sensors
                    .post_memory_limit(stats::limit_megabytes(memory.limit_bytes), memory.unlimited)
                {
                    failures.push(format!("Memory limit: {error}"));
                }
            }
            let node = sensors.node.clone();
            for failure in failures {
                if self.log_once.raise(&format!("post:{node}:{failure}")) {
                    self.logger
                        .error(format!("docker: {node}: value not posted: {failure}"));
                }
            }
        }
        Ok(())
    }

    fn read_failure(&mut self, what: &str, id: &str, key: &ServiceKey, error: &EngineError) {
        if self.log_once.raise(&format!("{what}:{id}")) {
            self.logger.warn(format!(
                "docker: {what} of {key} container {} failed ({error}); its values are skipped",
                short_id(id)
            ));
        }
    }

    fn clear_read_failure(&mut self, what: &str, id: &str) {
        if self.log_once.clear(&format!("{what}:{id}")) {
            self.logger.log(
                Level::Debug,
                &format!("docker: {what} of container {} works again", short_id(id)),
            );
        }
    }

    fn persist_if_dirty(&mut self) {
        if self.tracker.take_dirty() {
            self.persist();
        }
    }

    fn persist(&mut self) {
        let Some(path) = &self.state_path else {
            return;
        };
        match self.tracker.state.save(path) {
            Ok(()) => {
                self.log_once.clear("state-save");
            }
            Err(error) => {
                if self.log_once.raise("state-save") {
                    self.logger.error(format!(
                        "docker: cannot write the state file {} ({error}); restart baselines and \
                         OOM latches are kept in memory only",
                        path.display()
                    ));
                }
            }
        }
    }

    #[cfg(test)]
    pub(crate) fn tracker(&self) -> &Tracker {
        &self.tracker
    }
}

fn report_problems(logger: &Logger, log_once: &mut LogOnce, problems: &[String]) {
    for problem in problems {
        if log_once.raise(&format!("register:{problem}")) {
            logger.error(format!("docker: sensor {problem}"));
        }
    }
}

/// The next due time: one period on, or one period from now if the tick ran late — never a burst
/// of catch-up ticks.
fn advance(due: Instant, period: Duration, now: Instant) -> Instant {
    let next = due + period;
    if next <= now {
        now + period
    } else {
        next
    }
}

fn short_id(id: &str) -> &str {
    id.get(..12).unwrap_or(id)
}

fn unix_now() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_secs()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

/// `MemTotal` of this host, bytes, from `/proc/meminfo`.
pub fn host_mem_total() -> Option<u64> {
    std::fs::read_to_string("/proc/meminfo")
        .ok()
        .as_deref()
        .and_then(stats::parse_mem_total)
}

#[cfg(test)]
pub(crate) mod tests {
    use std::collections::HashMap;

    use super::engine::tests::{CONTAINERS, INSPECT, STATS_T0, STATS_T1, VERSION};
    use super::engine::{ContainerInspect, ContainerStats, ContainerSummary, VersionInfo};
    use super::*;
    use hsm_collector::CollectorOptions;

    /// Serves the garage captures: the listing, every inspect, and the two stats rounds (5.3 s
    /// apart on the host) in turn.
    pub(crate) struct FixtureEngine {
        pub list: Vec<ContainerSummary>,
        pub inspects: HashMap<String, ContainerInspect>,
        pub rounds: Vec<HashMap<String, ContainerStats>>,
        pub round: usize,
        pub down: bool,
    }

    impl FixtureEngine {
        pub fn garage() -> Self {
            let inspects: Vec<serde_json::Value> = serde_json::from_str(INSPECT).unwrap();
            Self {
                list: serde_json::from_str(CONTAINERS).unwrap(),
                inspects: inspects
                    .into_iter()
                    .map(|raw| {
                        let id = raw["Id"].as_str().unwrap().to_string();
                        (id, serde_json::from_value(raw).unwrap())
                    })
                    .collect(),
                rounds: vec![
                    serde_json::from_str(STATS_T0).unwrap(),
                    serde_json::from_str(STATS_T1).unwrap(),
                ],
                round: 0,
                down: false,
            }
        }

        fn check(&self) -> Result<(), EngineError> {
            if self.down {
                Err(EngineError::Unavailable(http::HttpError::Io(
                    std::io::Error::from(std::io::ErrorKind::ConnectionRefused),
                )))
            } else {
                Ok(())
            }
        }
    }

    impl EngineApi for FixtureEngine {
        fn version(&mut self) -> Result<VersionInfo, EngineError> {
            self.check()?;
            Ok(serde_json::from_str(VERSION).unwrap())
        }
        fn containers(&mut self) -> Result<Vec<ContainerSummary>, EngineError> {
            self.check()?;
            Ok(self.list.clone())
        }
        fn inspect(&mut self, id: &str) -> Result<ContainerInspect, EngineError> {
            self.check()?;
            self.inspects
                .get(id)
                .cloned()
                .ok_or_else(|| EngineError::Status {
                    status: 404,
                    message: format!("No such container: {id}"),
                })
        }
        fn stats(&mut self, id: &str) -> Result<ContainerStats, EngineError> {
            self.check()?;
            let round = &self.rounds[self.round.min(self.rounds.len() - 1)];
            round.get(id).cloned().ok_or_else(|| EngineError::Status {
                status: 404,
                message: format!("No such container: {id}"),
            })
        }
    }

    pub(crate) fn test_collector() -> Collector {
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        options.computer_name = Some("garage-server".into());
        options.module = Some("LinuxProbe".into());
        Collector::new(&options).expect("create")
    }

    fn quiet() -> Arc<Logger> {
        Arc::new(Logger::new(Level::Error, None))
    }

    const GARAGE_MEM_TOTAL: u64 = 16_298_872 * 1024;

    /// Drive the garage fixtures through one poll and two stats rounds, as the running source
    /// would over its first ~10 seconds.
    pub(crate) fn drive_garage(collector: &Collector) {
        let mut source = DockerSource::new(
            collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            quiet(),
            None,
            Some(GARAGE_MEM_TOTAL),
        );
        let never = || false;
        source.poll(&never).expect("poll");
        source.sample(&never).expect("first stats round");
        source.engine.round = 1;
        // The captures are ~5.3 s apart; model that interval on the source's monotonic clock.
        source.origin = source
            .origin
            .checked_sub(Duration::from_millis(5_300))
            .expect("monotonic clock past 5 s");
        source.sample(&never).expect("second stats round");
    }

    fn registered(collector: &Collector) -> Vec<String> {
        let mut paths: Vec<String> = collector
            .registrations()
            .iter()
            .filter_map(|json| {
                let start = json.find("\"Path\":\"")? + "\"Path\":\"".len();
                let end = json[start..].find('"')? + start;
                Some(json[start..end].to_string())
            })
            .collect();
        paths.sort();
        paths
    }

    #[test]
    fn the_garage_tree_has_no_empty_nodes() {
        let collector = test_collector();
        collector.start().expect("start");
        drive_garage(&collector);
        let paths = registered(&collector);
        collector.stop().expect("stop");

        let docker: Vec<&String> = paths.iter().filter(|p| p.contains("/Docker/")).collect();
        // 12 services × 3 state sensors + 4 healthchecks + 11 running services × 3 stats sensors.
        assert_eq!(docker.len(), 12 * 3 + 4 + 11 * 3, "{docker:#?}");
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(has(
            "garage-server/LinuxProbe/Docker/gitea/db/Service status"
        ));
        assert!(has("garage-server/LinuxProbe/Docker/gitea/db/Health"));
        assert!(has("garage-server/LinuxProbe/Docker/gitea/db/CPU"));
        // The exited one-shot image: state sensors only — it never ran while watched.
        assert!(has(
            "garage-server/LinuxProbe/Docker/lingua-ci/ci-image/Service status"
        ));
        assert!(!has(
            "garage-server/LinuxProbe/Docker/lingua-ci/ci-image/CPU"
        ));
        // No healthcheck, no Health node.
        assert!(!has("garage-server/LinuxProbe/Docker/hsm/app/Health"));
    }

    #[test]
    fn an_unreachable_daemon_backs_off_and_posts_nothing() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        engine.down = true;
        let mut source = DockerSource::new(
            &collector,
            engine,
            &DockerConfig::default(),
            quiet(),
            None,
            Some(GARAGE_MEM_TOTAL),
        );
        let error = source.poll(&|| false).expect_err("down");
        assert!(error.is_unavailable());
        assert!(
            registered(&collector).is_empty(),
            "no Docker sensor without Docker"
        );
        // The loop turns that into backoff and exits promptly on stop.
        let started = Instant::now();
        let stop_after = Instant::now() + Duration::from_millis(300);
        source.run(&|| Instant::now() >= stop_after);
        assert!(started.elapsed() < Duration::from_secs(3));
    }

    #[test]
    fn a_vanished_service_after_restart_registers_its_status_only() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        engine
            .list
            .retain(|c| c.label(identity::SERVICE_LABEL) != Some("portainer"));
        let mut state = State::default();
        let key = ServiceKey::new("portainer", "portainer");
        let mut record = state::ServiceRecord::new(&key);
        record.last_seen = unix_now() - 3_600;
        state.services.insert(key.clone(), record);

        let dir = std::env::temp_dir().join(format!("hsm-probe-docker-src-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let mut source = DockerSource::new(
            &collector,
            engine,
            &DockerConfig::default(),
            quiet(),
            Some(path.clone()),
            Some(GARAGE_MEM_TOTAL),
        );
        source.poll(&|| false).expect("poll");
        let portainer: Vec<String> = registered(&collector)
            .into_iter()
            .filter(|p| p.contains("/Docker/portainer/"))
            .collect();
        assert_eq!(
            portainer,
            vec!["garage-server/LinuxProbe/Docker/portainer/portainer/Service status"]
        );
        // The poll persisted the other services it met.
        let (saved, _) = State::load(&path);
        assert_eq!(saved.services.len(), 12);
        assert_eq!(source.tracker().state.services.len(), 12);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn unlabelled_containers_follow_compose_only() {
        let bare = ContainerSummary {
            id: "f".repeat(64),
            names: vec!["/adhoc".into()],
            state: "running".into(),
            labels: None,
        };
        for (compose_only, expected) in [(true, 0), (false, 3)] {
            let collector = test_collector();
            collector.start().expect("start");
            let mut engine = FixtureEngine::garage();
            engine.list = vec![bare.clone()];
            engine.inspects.insert(
                bare.id.clone(),
                serde_json::from_value(serde_json::json!({ "Id": bare.id, "RestartCount": 0,
                    "State": { "Status": "running", "OOMKilled": false } }))
                .unwrap(),
            );
            let config = DockerConfig {
                compose_only,
                ..DockerConfig::default()
            };
            let mut source = DockerSource::new(&collector, engine, &config, quiet(), None, None);
            source.poll(&|| false).expect("poll");
            let standalone = registered(&collector)
                .into_iter()
                .filter(|p| p.contains("/Docker/_standalone/adhoc/"))
                .count();
            assert_eq!(standalone, expected, "composeOnly={compose_only}");
        }
    }

    #[test]
    fn log_once_bounds_its_memory() {
        let mut once = LogOnce::default();
        assert!(once.raise("a"));
        assert!(!once.raise("a"));
        assert!(once.clear("a"));
        assert!(!once.clear("a"));
        for n in 0..(LOG_ONCE_CAPACITY * 3) {
            once.raise(&n.to_string());
        }
        assert!(once.active.len() <= LOG_ONCE_CAPACITY);
    }

    #[test]
    fn late_ticks_do_not_burst() {
        let start = Instant::now();
        let period = Duration::from_secs(5);
        assert_eq!(advance(start, period, start), start + period);
        let late = start + Duration::from_secs(60);
        assert_eq!(advance(start, period, late), late + period);
    }
}
