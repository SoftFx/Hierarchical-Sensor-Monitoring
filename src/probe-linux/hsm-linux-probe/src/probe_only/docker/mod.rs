//! The Docker Compose source (#1416): per-service CPU, memory, status, health, restarts, OOM
//! kills and block-device writes per hour under `<module>/Docker/<project>/<service>/…`, read from
//! the Docker Engine API over its Unix socket.
//!
//! A probe-only [`Source`] on a thread of its own with two cadences: stats every
//! `probe.docker.samplePeriodSec` (CPU and memory samples into 5-minute bars, write counters into
//! the running hour — [`written`]) and, on the first tick and every 60 s after, a state poll
//! (listing + inspect: status, health, restart count, OOM). Every tick first closes a clock hour
//! that has ended and posts its `Disk written per hour`, whether or not the daemon answers.
//! [`register`] primes the source before the collector starts: every service already running is
//! registered in the Start batch, alerts included; a service that appears later is registered at
//! runtime (the collector posts it, alerts included, from 0.9.1). The contract — paths, types, cadences, thresholds — is [`contract`]; the per-service
//! state machines are [`tracker`]; naming is [`identity`]; math is [`stats`]; the durable memory is
//! [`state`].
//!
//! Isolation (root rules #6/#8): the source never takes the probe down and never invents a value.
//! A panic inside a tick is caught by the probe-only runner; an unreachable daemon is logged once, retried with
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
pub mod written;

use std::collections::{BTreeMap, BTreeSet, HashSet};
use std::path::PathBuf;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use hsm_collector::{Collector, SensorStatus};

use crate::config::DockerConfig;
use crate::logging::{Level, Logger};
use crate::probe_only::Source;

use engine::{ApiVersion, EngineApi, EngineError};
use identity::{Membership, Naming, ServiceKey};
use sensors::ServiceSensors;
use state::{LoadOutcome, State};
use stats::{CpuCounters, CpuTracker};
use tracker::{ContainerObservation, InspectFacts, Tracker};
use written::WriteRecord;

pub use engine::Engine;

impl<T: EngineApi + ?Sized> EngineApi for Box<T> {
    fn set_api_version(&mut self, api: ApiVersion) {
        (**self).set_api_version(api)
    }
    fn version(&mut self) -> Result<engine::VersionInfo, EngineError> {
        (**self).version()
    }
    fn containers(&mut self) -> Result<Vec<engine::ContainerSummary>, EngineError> {
        (**self).containers()
    }
    fn inspect(&mut self, id: &str) -> Result<engine::ContainerInspect, EngineError> {
        (**self).inspect(id)
    }
    fn stats(&mut self, id: &str) -> Result<engine::ContainerStats, EngineError> {
        (**self).stats(id)
    }
}

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
    sample_period: Duration,
    oom_latch: Duration,
    compose_only: bool,
    /// `probe.docker.exclude` patterns.
    exclude: Vec<String>,
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
    /// When the next state poll is due (the first tick polls).
    next_poll: Instant,
    /// While the daemon is unreachable: no call before this.
    unavailable_until: Option<Instant>,
    backoff: Duration,
    /// A `Disk written per hour` accumulator changed since the state file was last written.
    written_dirty: bool,
    /// When the state file was last written.
    saved_at: Instant,
    should_stop: fn() -> bool,
    /// The wall clock, Unix milliseconds (a seam for the hour-boundary tests).
    clock: fn() -> i64,
    /// Stacked block devices (LVM, dm-crypt, md), so a write is counted on the physical disks.
    stacking: written::Stacking,
    /// Each listed container's `State.StartedAt` from the last inspect: a change means a restart,
    /// whose write counter began again from 0.
    started_at: std::collections::HashMap<String, String>,
    /// Some container on this host has reported a block-I/O write counter: the host accounts
    /// I/O per container, so a running container without one simply has not written yet.
    counters_seen: bool,
}

impl<'c, E: EngineApi> DockerSource<'c, E> {
    /// Build the source and load its state. `state_path = None` keeps the state in memory only.
    pub fn new(
        collector: &'c Collector,
        engine: E,
        config: &DockerConfig,
        logger: &Logger,
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
        // An excluded service is neither reported (no Stopped for it) nor remembered.
        let mut tracker = Tracker::new(state);
        let remembered: Vec<ServiceKey> = tracker.state.services.keys().cloned().collect();
        for key in remembered {
            if config
                .exclude
                .iter()
                .any(|pattern| identity::matches_pattern(pattern, &key))
                && tracker.forget(&key)
            {
                logger.info(format!(
                    "docker: {key}: excluded by probe.docker.exclude; dropped from the state"
                ));
            }
        }
        // Re-adopt the nodes of the previous run before anything new is named.
        let mut naming = Naming::default();
        for record in tracker.state.services.values() {
            if let Some(node) = &record.node {
                if !naming.adopt(&record.key(), node) {
                    logger.warn(format!(
                        "docker: remembered node {node} for {} is unusable; naming it afresh",
                        record.key()
                    ));
                }
            }
        }
        Self {
            collector,
            engine,
            sample_period: config.sample_period(),
            oom_latch: config.oom_latch(),
            compose_only: config.compose_only,
            exclude: config.exclude.clone(),
            state_path,
            host_mem_total,
            api: None,
            naming,
            tracker,
            sensors: BTreeMap::new(),
            roster: BTreeMap::new(),
            cpu: CpuTracker::default(),
            log_once: LogOnce::default(),
            origin: Instant::now(),
            next_poll: Instant::now(),
            unavailable_until: None,
            backoff: config.sample_period(),
            written_dirty: false,
            saved_at: Instant::now(),
            should_stop: crate::shutdown::is_requested,
            clock: unix_now_ms,
            stacking: written::Stacking::default(),
            started_at: std::collections::HashMap::new(),
            counters_seen: false,
        }
    }

    /// One scheduled tick: the state poll when it is due, then a stats round. An unreachable daemon
    /// is logged once and backed off (doubling up to [`contract::MAX_BACKOFF`]); ticks inside the
    /// backoff make no call at all. Nothing is posted for a failed read.
    fn tick(&mut self, logger: &Logger) {
        // Hours close on the wall clock, whether or not the daemon answers.
        self.roll_written_hours(logger);
        let now = Instant::now();
        if self.unavailable_until.is_some_and(|until| now < until) {
            return;
        }
        let should_stop = self.should_stop;
        let mut failure = None;
        if now >= self.next_poll {
            failure = self.poll(logger, &should_stop).err();
            self.next_poll = now + contract::STATE_POLL_PERIOD;
        }
        if failure.is_none() {
            failure = self.sample_stats(logger, &should_stop).err();
        }
        if self.written_dirty && self.saved_at.elapsed() >= contract::WRITTEN_PERSIST_PERIOD {
            self.persist(logger);
        }

        match failure {
            Some(error) => {
                if self.log_once.raise("engine") {
                    if error.is_socket_missing() {
                        // A host without Docker: nothing is wrong, nothing to report.
                        logger.info(format!(
                            "docker: {error}; no Docker on this host? The Docker source waits for \
                             the socket (set probe.docker.enabled = false to silence this)"
                        ));
                    } else {
                        let hint = if error.is_permission_denied() {
                            " — the probe needs the docker group: see docker-access.sh / the \
                             docker.conf drop-in in the README"
                        } else {
                            ""
                        };
                        logger.error(format!(
                            "docker: {error}{hint}; Docker values are skipped until it answers \
                             (retrying with backoff up to {} s)",
                            contract::MAX_BACKOFF.as_secs()
                        ));
                    }
                }
                self.unavailable_until = Some(Instant::now() + self.backoff);
                self.backoff = (self.backoff * 2).min(contract::MAX_BACKOFF);
                // Re-list first thing after the outage.
                self.next_poll = Instant::now();
            }
            None => {
                self.unavailable_until = None;
                self.backoff = self.sample_period;
                if self.log_once.clear("engine") {
                    logger.log(Level::Debug, "docker: the Engine API answers again");
                }
            }
        }
    }

    /// Register, before the collector starts, the sensors of every service the daemon lists now
    /// (and of services remembered as recently removed), so they ride the Start registration with
    /// their alerts. Posts nothing: a value before Start would be dropped, and the state machines
    /// advance from the first tick. A daemon that does not answer is not an error here — the
    /// services then register at runtime once it does.
    pub fn prime(&mut self, logger: &Logger) -> Result<usize, EngineError> {
        let Some((services, roster)) = self.observe(logger, &|| false)? else {
            return Ok(0);
        };
        // (has a healthcheck, has run)
        let present: BTreeMap<ServiceKey, (bool, bool)> = services
            .iter()
            .map(|(key, containers)| {
                let health = containers
                    .iter()
                    .any(|c| c.inspect.as_ref().is_some_and(|i| i.health.is_some()));
                (key.clone(), (health, roster.contains_key(key)))
            })
            .collect();

        let now = unix_now();
        let retention =
            i64::try_from(contract::VANISHED_SERVICE_RETENTION.as_secs()).unwrap_or(i64::MAX);
        let vanished: BTreeSet<ServiceKey> = self
            .tracker
            .state
            .services
            .values()
            .filter(|record| now.saturating_sub(record.last_seen) <= retention)
            .map(|record| record.key())
            .filter(|key| !present.contains_key(key))
            .collect();
        self.naming
            .resolve_all(present.keys().chain(vanished.iter()));

        let collector = self.collector;
        let mut registered = 0;
        for (key, (has_health, has_run)) in &present {
            // One stats read per running container, so `Memory used %` registers stating its limit
            // and `Disk written per hour` registers when the containers report writes.
            let (limit, writes) = if *has_run {
                roster
                    .get(key)
                    .map_or((None, false), |ids| self.first_stats_of(ids))
            } else {
                (None, false)
            };
            let sensors = self.sensors_of(key);
            let mut problems = sensors.ensure_state_sensors(collector, true, *has_health);
            if *has_run {
                problems.extend(sensors.ensure_stats_sensors(collector, limit, writes));
            }
            report_problems(logger, &mut self.log_once, &problems);
            registered += 1;
        }
        for key in &vanished {
            let problems = self
                .sensors_of(key)
                .ensure_state_sensors(collector, false, false);
            report_problems(logger, &mut self.log_once, &problems);
            registered += 1;
        }
        Ok(registered)
    }

    /// From one stats read of each container: the service's memory limit (MB, unlimited) — `None`
    /// when a read fails (the first stats round then sets it) — and whether any container reports
    /// a block-device write counter.
    fn first_stats_of(&mut self, ids: &[String]) -> (Option<(i32, bool)>, bool) {
        let mut readings = Vec::with_capacity(ids.len());
        let mut complete = true;
        let mut writes = false;
        for id in ids {
            let stats = match self.engine.stats(id) {
                Ok(stats) => stats,
                // A daemon or container that does not answer costs one timeout per service, not
                // one per replica, before Start; the first stats round catches up.
                Err(error) if error.is_unavailable() => {
                    complete = false;
                    break;
                }
                Err(_) => {
                    complete = false;
                    continue;
                }
            };
            writes |= written::written_bytes(&stats, &mut self.stacking).is_some();
            match stats::memory_reading(&stats, self.host_mem_total) {
                Some(reading) => readings.push(reading),
                None => complete = false,
            }
        }
        let limit = complete
            .then(|| stats::service_memory(&readings, self.host_mem_total))
            .flatten()
            .map(|memory| (stats::limit_megabytes(memory.limit_bytes), memory.unlimited));
        (limit, writes)
    }

    /// Close every service's `Disk written per hour` hour that has ended and post it. Only the hour
    /// that just ended is posted; an older one (the probe was down when it ended) is dropped with a
    /// log line — it would arrive hours late under the current time. The state file is written at
    /// once, so a restart cannot post the same hour twice. Returns the hours handed to the collector.
    fn roll_written_hours(&mut self, logger: &Logger) -> Vec<(ServiceKey, written::CompletedHour)> {
        let now_ms = (self.clock)();
        let current = written::hour_start(now_ms);
        let mut rolled = false;
        let mut posted = Vec::new();
        for (key, record) in self.tracker.state.services.iter_mut() {
            let Some(accumulator) = record.written.as_mut() else {
                continue;
            };
            if accumulator.hour_start >= current {
                continue;
            }
            rolled = true;
            let Some(done) = accumulator.roll(now_ms) else {
                continue;
            };
            let node = record.node.clone().unwrap_or_else(|| self.naming.node(key));
            if done.hour_start + 3600 != current {
                logger.info(format!(
                    "docker: {node}: Disk written per hour for {} not posted: it ended more \
                     than an hour ago (the probe was not running at the next hour boundary); only \
                     the hour that has just ended is posted after a restart",
                    done.comment()
                ));
                continue;
            }
            // A measured hour means the service reported writes, so its sensor exists on the
            // server; it may just not be registered in this run yet (restarted while the daemon
            // did not answer): register it now rather than drop the hour.
            let sensors = self
                .sensors
                .entry(key.clone())
                .or_insert_with(|| ServiceSensors::new(node.clone()));
            if let Some(problem) = sensors.ensure_disk_written(self.collector) {
                if self.log_once.raise(&format!("register:{problem}")) {
                    logger.error(format!(
                        "docker: sensor {problem}; Disk written per hour for {} is lost",
                        done.comment()
                    ));
                }
                continue;
            }
            let Some(sensor) = sensors.disk_written.as_ref() else {
                continue;
            };
            let comment = done.comment();
            match sensor.add_with(done.megabytes(), SensorStatus::Ok, Some(&comment)) {
                Ok(()) => {
                    logger.log(
                        Level::Debug,
                        &format!(
                            "docker: {node}: Disk written per hour {} MB ({comment})",
                            done.megabytes()
                        ),
                    );
                    posted.push((key.clone(), done));
                }
                Err(error) => {
                    if self.log_once.raise(&format!("post:{node}:written")) {
                        logger.error(format!(
                            "docker: {node}: value not posted: Disk written per hour: {error}"
                        ));
                    }
                }
            }
        }
        if rolled {
            self.written_dirty = true;
            self.persist(logger);
        }
        posted
    }

    fn sensors_of(&mut self, key: &ServiceKey) -> &mut ServiceSensors<'c> {
        let node = self.naming.node(key);
        self.sensors
            .entry(key.clone())
            .or_insert_with(|| ServiceSensors::new(node))
    }

    /// The service a listed container belongs to, or `None` (logged once) when it is not
    /// monitored: a `docker compose run` one-off, or unlabelled with `composeOnly`.
    fn member(
        &mut self,
        logger: &Logger,
        container: &engine::ContainerSummary,
    ) -> Option<ServiceKey> {
        let reason = match identity::membership(container, self.compose_only) {
            Membership::Service(key) => return Some(key),
            Membership::OneOff => {
                "is a `docker compose run` one-off; not monitored as a replica of \
                                   its service"
            }
            Membership::Skipped => {
                "has no Compose labels; not monitored (probe.docker.composeOnly)"
            }
        };
        if self.log_once.raise(&format!("skip:{}", container.id)) {
            logger.info(format!(
                "docker: container '{}' ({}) {reason}",
                container.name(),
                short_id(&container.id)
            ));
        }
        None
    }

    /// List and inspect: every monitored container, grouped by service, plus the containers to
    /// sample (running or paused). `None` when a stop was requested midway. Completed one-shot jobs
    /// (see [`tracker::completed_job`]) that are not already known as services are left out.
    #[allow(clippy::type_complexity)]
    fn observe(
        &mut self,
        logger: &Logger,
        should_stop: &dyn Fn() -> bool,
    ) -> Result<
        Option<(
            BTreeMap<ServiceKey, Vec<ContainerObservation>>,
            BTreeMap<ServiceKey, Vec<String>>,
        )>,
        EngineError,
    > {
        self.ensure_api(logger)?;
        let containers = self.engine.containers()?;

        let mut services: BTreeMap<ServiceKey, Vec<ContainerObservation>> = BTreeMap::new();
        let mut roster: BTreeMap<ServiceKey, Vec<String>> = BTreeMap::new();
        for container in &containers {
            let Some(key) = self.member(logger, container) else {
                continue;
            };
            // Excluded services (probe.docker.exclude): not monitored at all — not even inspected.
            if self
                .exclude
                .iter()
                .any(|pattern| identity::matches_pattern(pattern, &key))
            {
                self.tracker.forget(&key);
                if self.log_once.raise(&format!("exclude:{key}")) {
                    logger.info(format!(
                        "docker: {key}: excluded by probe.docker.exclude, not monitored"
                    ));
                }
                continue;
            }
            if should_stop() {
                return Ok(None);
            }
            let inspect = match self.engine.inspect(&container.id) {
                Ok(inspect) => {
                    self.clear_read_failure(logger, "inspect", &container.id);
                    if !inspect.state.started_at.is_empty() {
                        self.started_at
                            .insert(container.id.clone(), inspect.state.started_at.clone());
                    }
                    Some(InspectFacts {
                        restart_count: inspect.restart_count,
                        oom_killed: inspect.state.oom_killed,
                        health: inspect.state.health.map(|health| health.status),
                        exit_code: inspect.state.exit_code,
                        restart_policy: inspect.host_config.restart_policy.name,
                    })
                }
                // A timeout here may be this one container wedged: skip it, keep the others.
                Err(error) if error.is_unavailable() && !error.is_timeout() => return Err(error),
                Err(error) => {
                    self.read_failure(logger, "inspect", &container.id, &key, &error);
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
                logger.warn(format!(
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

        // A service first seen as a finished one-shot job is not a service. One that the state
        // already knows stays a service (its Stopped is real), and a job whose container runs
        // again becomes one from then on.
        let jobs: Vec<ServiceKey> = services
            .iter()
            .filter(|(key, _)| !self.tracker.state.services.contains_key(*key))
            .filter_map(
                |(key, containers)| match tracker::completed_job(containers) {
                    Some(false) => None,
                    Some(true) => Some((key.clone(), true)),
                    None => Some((key.clone(), false)),
                },
            )
            .map(|(key, decided)| {
                if decided && self.log_once.raise(&format!("job:{key}")) {
                    logger.info(format!(
                        "docker: {key}: completed job (exit 0, no restart policy), not monitored"
                    ));
                }
                key
            })
            .collect();
        for key in &jobs {
            services.remove(key);
            roster.remove(key);
        }
        Ok(Some((services, roster)))
    }

    fn ensure_api(&mut self, logger: &Logger) -> Result<(), EngineError> {
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
        logger.info(format!(
            "docker: Engine {} (API {min}..={max}); speaking API {api}",
            version.version
        ));
        self.api = Some(api);
        Ok(())
    }

    /// The 60-second state poll: list, inspect, advance the state machines, post.
    pub fn poll(
        &mut self,
        logger: &Logger,
        should_stop: &dyn Fn() -> bool,
    ) -> Result<(), EngineError> {
        let Some((services, roster)) = self.observe(logger, should_stop)? else {
            return Ok(());
        };

        let outcome = self.tracker.observe(&services, unix_now(), self.oom_latch);
        // Stacks change rarely (an LVM volume added); re-read them once a poll.
        self.stacking.refresh();

        self.naming
            .resolve_all(services.keys().chain(outcome.reports.keys()));
        for key in outcome.reports.keys() {
            let node = self.naming.node(key);
            self.tracker.set_node(key, &node);
        }

        for key in &outcome.forgotten {
            logger.info(format!(
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
            report_problems(logger, &mut self.log_once, &problems);

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
                    logger.error(format!("docker: {node}: value not posted: {failure}"));
                }
            }
        }

        for (key, record) in self.tracker.state.services.iter_mut() {
            if let Some(accumulator) = record.written.as_mut() {
                let before = accumulator.baselines.len();
                accumulator.retain(roster.get(key).map_or(&[][..], Vec::as_slice));
                self.written_dirty |= accumulator.baselines.len() != before;
            }
        }
        self.started_at.retain(|id, _| {
            roster
                .values()
                .any(|ids| ids.iter().any(|listed| listed == id))
        });
        self.roster = roster;
        self.cpu.retain(|id| {
            self.roster
                .values()
                .any(|ids| ids.iter().any(|listed| listed == id))
        });
        self.persist_if_dirty(logger);
        Ok(())
    }

    /// The stats tick: one one-shot stats call per sampled container, CPU and memory into bars.
    pub fn sample_stats(
        &mut self,
        logger: &Logger,
        should_stop: &dyn Fn() -> bool,
    ) -> Result<(), EngineError> {
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
            // Disk written: the longest interval among this round's accepted deltas (the deltas
            // themselves go into the hour as they are read), and whether any container reported a
            // write counter at all.
            let mut written_gap: Option<u64> = None;
            let mut any_writes = false;

            for id in ids {
                if should_stop() {
                    return Ok(());
                }
                let stats = match self.engine.stats(id) {
                    Ok(stats) => {
                        self.clear_read_failure(logger, "stats", id);
                        stats
                    }
                    Err(error) if error.is_unavailable() && !error.is_timeout() => {
                        return Err(error)
                    }
                    Err(error) => {
                        // The write baseline is kept: the counter is cumulative, so the next good
                        // read still yields the bytes written meanwhile.
                        self.read_failure(logger, "stats", id, key, &error);
                        self.cpu.forget(id);
                        cpu_valid = false;
                        memory_valid = false;
                        continue;
                    }
                };
                let now_ms = (self.clock)();
                let counter = written::written_bytes(&stats, &mut self.stacking);
                let running = stats.cpu_stats.system_cpu_usage.is_some();
                self.counters_seen |= counter.is_some();
                let started_at = self.started_at.get(id).map(String::as_str);
                let mut restarted = false;
                let mut discarded: Option<&str> = None;
                if let Some(record) = self.tracker.state.services.get_mut(key) {
                    match counter {
                        // Only a host that reports write counters keeps (and rewrites) a record.
                        Some(counter) => {
                            let accumulator = record
                                .written
                                .get_or_insert_with(|| WriteRecord::new(now_ms));
                            self.written_dirty = true;
                            any_writes = true;
                            match accumulator.sample(
                                id,
                                counter,
                                now_ms,
                                self.sample_period,
                                started_at,
                            ) {
                                Ok((_, gap)) => {
                                    written_gap = Some(written_gap.unwrap_or(0).max(gap));
                                }
                                Err(written::Skip::Restarted) => restarted = true,
                                Err(written::Skip::ClockBackwards) => {
                                    discarded = Some("the clock went backwards")
                                }
                                Err(written::Skip::GapAcrossHours) => {
                                    discarded = Some(
                                        "no sample across an hour boundary (the probe was not \
                                         running or the daemon did not answer)",
                                    )
                                }
                                Err(written::Skip::HourNotRolled) => logger.log(
                                    Level::Debug,
                                    &format!(
                                        "docker: {key}: write sample after the hour boundary \
                                         left for the next tick"
                                    ),
                                ),
                                Err(_) => {}
                            }
                        }
                        // Running, and this host accounts block I/O (another container reported a
                        // counter): nothing written yet, so its counter is 0 since it started.
                        None if self.counters_seen && running && started_at.is_some() => {
                            let accumulator = record
                                .written
                                .get_or_insert_with(|| WriteRecord::new(now_ms));
                            accumulator.zero_baseline(id, now_ms, started_at.unwrap_or_default());
                            self.written_dirty = true;
                        }
                        None => {
                            if let Some(accumulator) = record.written.as_mut() {
                                self.written_dirty |= accumulator.forget(id);
                            }
                        }
                    }
                }
                if let Some(reason) = discarded {
                    // Once per service, reason and hour: the skip only sets a new baseline.
                    let hour = written::hour_start(now_ms);
                    if self
                        .log_once
                        .raise(&format!("written-skip:{key}:{reason}:{hour}"))
                    {
                        logger.info(format!(
                            "docker: {key}: a Disk written per hour sample was discarded \
                             ({reason}); the writes since the previous sample are not counted"
                        ));
                    }
                }
                if restarted
                    && self.log_once.raise(&format!(
                        "restarted:{id}:{}",
                        started_at.unwrap_or_default()
                    ))
                {
                    logger.info(format!(
                        "docker: {key}: container {} was restarted since its last sample; its \
                         writes between that sample and the restart are not counted in Disk \
                         written per hour",
                        short_id(id)
                    ));
                }
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

            if let Some(gap) = written_gap {
                let now_ms = (self.clock)();
                if let Some(accumulator) = self
                    .tracker
                    .state
                    .services
                    .get_mut(key)
                    .and_then(|record| record.written.as_mut())
                {
                    accumulator.cover(gap, now_ms);
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
            let memory = memory_valid
                .then(|| stats::service_memory(&readings, self.host_mem_total))
                .flatten();
            let limit = memory.map(|m| (stats::limit_megabytes(m.limit_bytes), m.unlimited));
            let problems = sensors.ensure_stats_sensors(self.collector, limit, any_writes);
            report_problems(logger, &mut self.log_once, &problems);

            let mut failures = Vec::new();
            logger.log(
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
                if let Some(limit) = limit {
                    if let Err(error) = sensors.follow_memory_limit(limit) {
                        failures.push(format!("Memory used % description: {error}"));
                    }
                }
            }
            let node = sensors.node.clone();
            for failure in failures {
                if self.log_once.raise(&format!("post:{node}:{failure}")) {
                    logger.error(format!("docker: {node}: value not posted: {failure}"));
                }
            }
        }
        Ok(())
    }

    fn read_failure(
        &mut self,
        logger: &Logger,
        what: &str,
        id: &str,
        key: &ServiceKey,
        error: &EngineError,
    ) {
        if self.log_once.raise(&format!("{what}:{id}")) {
            logger.warn(format!(
                "docker: {what} of {key} container {} failed ({error}); its values are skipped",
                short_id(id)
            ));
        }
    }

    fn clear_read_failure(&mut self, logger: &Logger, what: &str, id: &str) {
        if self.log_once.clear(&format!("{what}:{id}")) {
            logger.log(
                Level::Debug,
                &format!("docker: {what} of container {} works again", short_id(id)),
            );
        }
    }

    fn persist_if_dirty(&mut self, logger: &Logger) {
        if self.tracker.take_dirty() {
            self.persist(logger);
        }
    }

    fn persist(&mut self, logger: &Logger) {
        let Some(path) = &self.state_path else {
            return;
        };
        self.saved_at = Instant::now();
        match self.tracker.state.save(path) {
            Ok(()) => {
                self.written_dirty = false;
                self.log_once.clear("state-save");
            }
            Err(error) => {
                if self.log_once.raise("state-save") {
                    logger.error(format!(
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

fn short_id(id: &str) -> &str {
    id.get(..12).unwrap_or(id)
}

fn unix_now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

fn unix_now() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_secs()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}

impl<E: EngineApi + Send> Source for DockerSource<'_, E> {
    fn name(&self) -> &'static str {
        "docker"
    }

    fn period(&self) -> Duration {
        self.sample_period
    }

    fn sample(&mut self, logger: &Logger) {
        self.tick(logger);
    }

    fn stop(&mut self, logger: &Logger) {
        // The running hour's accumulator, so a restart continues it.
        if self.written_dirty {
            self.persist(logger);
        }
    }
}

/// Build the Docker source, prime its registrations (before Start) and hand it to the probe-only
/// runner. `None` when disabled.
pub fn register<'c>(
    collector: &'c Collector,
    config: &DockerConfig,
    engine: Box<dyn EngineApi + Send>,
    state_path: Option<PathBuf>,
    sys_root: PathBuf,
    logger: &Logger,
) -> Option<Box<dyn Source + 'c>> {
    if !config.enabled {
        logger.info("docker: source disabled (probe.docker.enabled = false)");
        return None;
    }
    let mut source = DockerSource::new(
        collector,
        engine,
        config,
        logger,
        state_path,
        host_mem_total(),
    );
    source.stacking = written::Stacking::new(Some(sys_root));
    match source.prime(logger) {
        Ok(count) => logger.info(format!(
            "docker: {count} Compose service(s) registered (socket {}, stats every {} s, state \
             poll every {} s)",
            config.socket.display(),
            config.sample_period_sec,
            contract::STATE_POLL_PERIOD.as_secs()
        )),
        // Logged once by the first tick with the right severity (missing socket vs permission).
        Err(error) => logger.info(format!(
            "docker: nothing registered before start ({error}); services register once the \
             Engine API answers"
        )),
    }
    Some(Box::new(source))
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
        /// Container id prefix whose inspect and stats calls time out (a wedged container).
        pub wedged: Option<&'static str>,
        /// Stats calls made, for the bounded-priming test.
        pub stats_calls: usize,
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
                wedged: None,
                stats_calls: 0,
            }
        }

        fn check_container(&self, id: &str) -> Result<(), EngineError> {
            self.check()?;
            match self.wedged {
                Some(prefix) if id.starts_with(prefix) => {
                    Err(EngineError::Unavailable(http::HttpError::Timeout))
                }
                _ => Ok(()),
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
            self.check_container(id)?;
            self.inspects
                .get(id)
                .cloned()
                .ok_or_else(|| EngineError::Status {
                    status: 404,
                    message: format!("No such container: {id}"),
                })
        }
        fn stats(&mut self, id: &str) -> Result<ContainerStats, EngineError> {
            self.stats_calls += 1;
            self.check_container(id)?;
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
        Collector::new(&options).expect("create")
    }

    fn quiet() -> Logger {
        Logger::new(Level::Error, None)
    }

    /// A source over the garage captures whose stop check never fires (the process-global
    /// shutdown flag is set by another unit test).
    pub(crate) fn garage_source<'c>(
        collector: &'c Collector,
        engine: FixtureEngine,
        config: &DockerConfig,
        state_path: Option<PathBuf>,
    ) -> DockerSource<'c, FixtureEngine> {
        let mut source = DockerSource::new(
            collector,
            engine,
            config,
            &quiet(),
            state_path,
            Some(GARAGE_MEM_TOTAL),
        );
        source.should_stop = || false;
        source
    }

    const GARAGE_MEM_TOTAL: u64 = 16_298_872 * 1024;

    /// Drive the garage fixtures through one poll and two stats rounds, as the running source
    /// would over its first ~10 seconds.
    pub(crate) fn drive_garage(collector: &Collector) {
        drive_garage_on(collector, |_| {});
    }

    /// `drive_garage`, then `more` on the same source.
    fn drive_garage_on(
        collector: &Collector,
        more: impl FnOnce(&mut DockerSource<'_, FixtureEngine>),
    ) {
        let mut source = garage_source(
            collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        let never = || false;
        source.poll(&quiet(), &never).expect("poll");
        source
            .sample_stats(&quiet(), &never)
            .expect("first stats round");
        source.engine.round = 1;
        // The captures are ~5.3 s apart; model that interval on the source's monotonic clock.
        source.origin = source
            .origin
            .checked_sub(Duration::from_millis(5_300))
            .expect("monotonic clock past 5 s");
        source
            .sample_stats(&quiet(), &never)
            .expect("second stats round");
        more(&mut source);
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

        let docker: Vec<&String> = paths.iter().filter(|p| p.starts_with("Docker/")).collect();
        // 11 services × 3 state sensors + 4 healthchecks + 11 × 3 stats sensors (CPU, Memory used %,
        // Disk written per hour); the twelfth, lingua-ci/ci-image, is a completed one-shot job and
        // not monitored at all.
        assert_eq!(docker.len(), 11 * 3 + 4 + 11 * 3, "{docker:#?}");
        assert!(paths.iter().all(|p| !p.ends_with("/Memory limit")));
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(has("Docker/gitea/db/Service status"));
        assert!(has("Docker/gitea/db/Health"));
        assert!(has("Docker/gitea/db/CPU"));
        // The exited one-shot image build (Exited (0), restart "no"): a completed job, no node.
        assert!(paths
            .iter()
            .all(|p| !p.starts_with("Docker/lingua-ci/ci-image/")));
        // No healthcheck, no Health node.
        assert!(!has("Docker/hsm/app/Health"));
    }

    #[test]
    fn an_unreachable_daemon_backs_off_and_posts_nothing() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        engine.down = true;
        let mut source = garage_source(&collector, engine, &DockerConfig::default(), None);
        let error = source.poll(&quiet(), &|| false).expect_err("down");
        assert!(error.is_unavailable());
        assert!(
            registered(&collector).is_empty(),
            "no Docker sensor without Docker"
        );
        // A scheduled tick turns that into a backoff: the next tick makes no call at all.
        Source::sample(&mut source, &quiet());
        let until = source.unavailable_until.expect("backing off");
        assert!(until > Instant::now());
        assert_eq!(
            source.backoff,
            Duration::from_secs(10),
            "doubled from the 5 s period"
        );
        Source::sample(&mut source, &quiet());
        assert_eq!(
            source.unavailable_until,
            Some(until),
            "no retry inside the backoff"
        );
        // The daemon comes back: once the backoff has passed, the next tick recovers.
        source.engine.down = false;
        source.unavailable_until = Some(Instant::now());
        Source::sample(&mut source, &quiet());
        assert_eq!(source.unavailable_until, None);
        assert_eq!(source.backoff, Duration::from_secs(5));
        // Registered at runtime (the collector was already running), alerts included.
        let status = collector
            .registrations()
            .into_iter()
            .find(|json| json.contains("Docker/gitea/db/Service status"))
            .expect("gitea/db registered at runtime");
        assert!(status.contains("$operation Running"), "{status}");
    }

    /// The garage tree primed before Start: everything registers in the Start batch.
    #[test]
    fn prime_registers_the_whole_tree_before_start_with_alerts() {
        let collector = test_collector();
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        assert_eq!(source.prime(&quiet()).expect("prime"), 11);
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        let docker: Vec<&String> = registrations
            .iter()
            .filter(|j| j.contains("\"Path\":\"Docker/"))
            .collect();
        assert_eq!(docker.len(), 70);

        let find = |path: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("\"Path\":\"Docker/{path}\"")))
                .unwrap_or_else(|| panic!("{path} not registered"))
                .clone()
        };
        // Service status: the Windows ServiceStatusPrototype alert, byte for byte (the same text
        // the native collector's own service-status registration golden pins).
        let status = find("gitea/db/Service status");
        assert!(
            status.contains(
                "\"Alerts\":[{\"Conditions\":[{\"Combination\":0,\"Operation\":5,\"Property\":20,\
                 \"Target\":{\"Type\":0,\"Value\":\"4\"}}],\"Status\":1,\"DestinationMode\":3,\
                 \"Template\":\"[$product]$path $operation Running\",\"Icon\":null,\"IsDisabled\":false,\
                 \"ConfirmationPeriod\":3000000000,\"ScheduledNotificationTime\":\"0001-01-01T12:00:00Z\",\
                 \"ScheduledRepeatMode\":20,\"ScheduledInstantSend\":true}]"
            ),
            "{status}"
        );
        for path in [
            "gitea/db/CPU",
            "gitea/db/Memory used %",
            "gitea/db/Health",
            "gitea/db/Restart count",
            "gitea/db/OOM killed",
        ] {
            assert!(
                find(path).contains("\"Alerts\":[{"),
                "{path} carries its alert"
            );
        }
        // The limit lives in the Memory used % description (gitea/db: 1 GiB).
        assert!(
            find("gitea/db/Memory used %").contains("**1024 MB** on this host"),
            "{}",
            find("gitea/db/Memory used %")
        );
        // Disk written per hour: decimal MB, EMA statistics, no alert, and the one-hour offset stated.
        let written = find("gitea/db/Disk written per hour");
        assert!(written.contains("\"OriginalUnit\":3"), "{written}");
        assert!(written.contains("\"Statistics\":1"), "{written}");
        assert!(!written.contains("\"Alerts\":[{"), "{written}");
        assert!(
            written.contains("sent just after the hour it covers"),
            "{written}"
        );
        // The state sensors store only changes on the server.
        for path in [
            "gitea/db/Service status",
            "gitea/db/Health",
            "gitea/db/OOM killed",
        ] {
            assert!(
                find(path).contains("\"AggregateData\":true"),
                "{path}: {}",
                find(path)
            );
        }
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

        let mut source = garage_source(
            &collector,
            engine,
            &DockerConfig::default(),
            Some(path.clone()),
        );
        source.poll(&quiet(), &|| false).expect("poll");
        let portainer: Vec<String> = registered(&collector)
            .into_iter()
            .filter(|p| p.starts_with("Docker/portainer/"))
            .collect();
        assert_eq!(portainer, vec!["Docker/portainer/portainer/Service status"]);
        // The poll persisted the other services it met: 10 listed services (ci-image is a
        // completed job) plus the remembered portainer.
        let (saved, _) = State::load(&path);
        assert_eq!(saved.services.len(), 11);
        assert_eq!(source.tracker().state.services.len(), 11);
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
            let mut source = garage_source(&collector, engine, &config, None);
            source.poll(&quiet(), &|| false).expect("poll");
            let standalone = registered(&collector)
                .into_iter()
                .filter(|p| p.starts_with("Docker/_standalone/adhoc/"))
                .count();
            assert_eq!(standalone, expected, "composeOnly={compose_only}");
        }
    }

    #[test]
    fn a_wedged_container_does_not_blank_the_others() {
        // gitea-db's inspect and stats time out; every other service still reports, and db
        // keeps its status (from the listing) but posts no guessed health or stats.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        engine.wedged = Some("abbd59dcacc8");
        let mut source = garage_source(&collector, engine, &DockerConfig::default(), None);
        source
            .poll(&quiet(), &|| false)
            .expect("a timeout on one container is not an outage");
        source
            .sample_stats(&quiet(), &|| false)
            .expect("nor in the stats tick");
        let paths = registered(&collector);
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(has("Docker/gitea/db/Service status"));
        assert!(!has("Docker/gitea/db/Health"));
        assert!(!has("Docker/gitea/db/CPU"));
        assert!(has("Docker/gitea/gitea/Health"));
        assert!(has("Docker/gitea/gitea/CPU"));
    }

    #[test]
    fn nodes_are_remembered_in_the_state_and_readopted() {
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-nodes-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut source = garage_source(
                &collector,
                FixtureEngine::garage(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            source.poll(&quiet(), &|| false).expect("poll");
        }
        let (saved, _) = State::load(&path);
        let db = &saved.services[&ServiceKey::new("gitea", "db")];
        assert_eq!(db.node.as_deref(), Some("Docker/gitea/db"));

        // A restarted source adopts every remembered node before naming anything.
        let collector = test_collector();
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            Some(path.clone()),
        );
        assert_eq!(
            source.naming.node(&ServiceKey::new("gitea", "db")),
            "Docker/gitea/db"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn excluded_services_are_not_monitored_and_leave_the_state() {
        let collector = test_collector();
        collector.start().expect("start");
        // portainer was monitored before (it is in the state); now both it and janitor are excluded.
        let mut state = State::default();
        let portainer = ServiceKey::new("portainer", "portainer");
        let mut record = state::ServiceRecord::new(&portainer);
        record.last_seen = unix_now() - 60;
        state.services.insert(portainer.clone(), record);
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-exclude-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let config = DockerConfig {
            exclude: vec!["portainer/*".into(), "lingua-ci/janitor".into()],
            ..DockerConfig::default()
        };
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &config,
            Some(path.clone()),
        );
        assert!(
            !source.tracker().state.services.contains_key(&portainer),
            "dropped from the state at start: no Stopped, no alerts"
        );
        source.poll(&quiet(), &|| false).expect("poll");
        source.sample_stats(&quiet(), &|| false).expect("stats");
        let paths = registered(&collector);
        assert!(paths.iter().all(|p| !p.starts_with("Docker/portainer/")));
        assert!(paths
            .iter()
            .all(|p| !p.starts_with("Docker/lingua-ci/janitor/")));
        assert!(paths
            .iter()
            .any(|p| p.starts_with("Docker/lingua-ci/runner-heavy/")));
        let (saved, _) = State::load(&path);
        assert!(!saved.services.contains_key(&portainer));
        assert!(!saved
            .services
            .contains_key(&ServiceKey::new("lingua-ci", "janitor")));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_changed_memory_limit_rewrites_the_memory_used_description() {
        let collector = test_collector();
        collector.start().expect("start");
        drive_garage_on(&collector, |source| {
            // hsm/app's container is recreated with mem_limit 512m.
            for round in &mut source.engine.rounds {
                for (id, stats) in round.iter_mut() {
                    if id.starts_with("c5abbadb3674") {
                        stats.memory_stats.limit = Some(512 * 1024 * 1024);
                    }
                }
            }
            source.origin = source
                .origin
                .checked_sub(Duration::from_millis(5_300))
                .expect("clock");
            source
                .sample_stats(&quiet(), &|| false)
                .expect("third round");
        });
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        let app: Vec<&String> = registrations
            .iter()
            .filter(|json| json.contains("Docker/hsm/app/Memory used %"))
            .collect();
        assert_eq!(app.len(), 1, "re-registered in place, not added");
        assert!(app[0].contains("**512 MB** on this host"), "{}", app[0]);
    }

    const CI_IMAGE: &str = "Docker/lingua-ci/ci-image/Service status";

    fn set_ci_image_state(engine: &mut FixtureEngine, state: &str) {
        let ci = engine
            .list
            .iter_mut()
            .find(|c| c.label(identity::SERVICE_LABEL) == Some("ci-image"))
            .expect("ci-image listed");
        ci.state = state.into();
    }

    #[test]
    fn a_completed_job_is_not_monitored_until_it_runs_again() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        source.poll(&quiet(), &|| false).expect("poll");
        assert!(!registered(&collector).iter().any(|p| p == CI_IMAGE));
        let job = ServiceKey::new("lingua-ci", "ci-image");
        assert!(!source.tracker().state.services.contains_key(&job));

        // The job is rebuilt: a running container makes it a service from then on...
        set_ci_image_state(&mut source.engine, "running");
        source.poll(&quiet(), &|| false).expect("poll");
        assert!(registered(&collector).iter().any(|p| p == CI_IMAGE));
        // ...so its next Exited (0) is a real Stopped, not a job to forget.
        set_ci_image_state(&mut source.engine, "exited");
        source.poll(&quiet(), &|| false).expect("poll");
        assert!(source.tracker().state.services.contains_key(&job));
    }

    #[test]
    fn a_service_the_state_knows_stays_a_service_when_it_exits_zero() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut state = State::default();
        let job = ServiceKey::new("lingua-ci", "ci-image");
        let mut record = state::ServiceRecord::new(&job);
        record.last_seen = unix_now() - 60;
        state.services.insert(job, record);
        let dir = std::env::temp_dir().join(format!("hsm-probe-docker-job-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            Some(path),
        );
        source.poll(&quiet(), &|| false).expect("poll");
        assert!(registered(&collector).iter().any(|p| p == CI_IMAGE));
        let _ = std::fs::remove_dir_all(&dir);
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

    thread_local! {
        /// The wall clock of the hour-boundary tests (each test runs on its own thread).
        static NOW_MS: std::cell::Cell<i64> = const { std::cell::Cell::new(0) };
    }

    fn test_clock() -> i64 {
        NOW_MS.with(std::cell::Cell::get)
    }

    fn set_clock(unix_ms: i64) {
        NOW_MS.with(|now| now.set(unix_ms));
    }

    /// 2026-09-29 13:00:00 UTC, ms.
    const H13: i64 = 1_790_686_800_000;
    const GITEA_DB: &str = "abbd59dcacc8";

    fn key_of(project: &str, service: &str) -> ServiceKey {
        ServiceKey::new(project, service)
    }

    /// Poll, then the two garage stats rounds `apart_ms` apart starting at `at_ms`.
    fn two_rounds(source: &mut DockerSource<'_, FixtureEngine>, at_ms: i64, apart_ms: i64) {
        let never = || false;
        set_clock(at_ms);
        source.engine.round = 0;
        source.poll(&quiet(), &never).expect("poll");
        source.sample_stats(&quiet(), &never).expect("round 0");
        set_clock(at_ms + apart_ms);
        source.engine.round = 1;
        source.sample_stats(&quiet(), &never).expect("round 1");
    }

    fn add_written(engine: &mut FixtureEngine, round: usize, prefix: &str, bytes: u64) {
        let stats = engine.rounds[round]
            .iter_mut()
            .find(|(id, _)| id.starts_with(prefix))
            .unwrap()
            .1;
        let entries = stats
            .blkio_stats
            .io_service_bytes_recursive
            .as_mut()
            .unwrap();
        let write = entries.iter_mut().find(|e| e.op == "write").unwrap();
        write.value += bytes;
    }

    #[test]
    fn disk_written_is_posted_once_just_after_its_hour_with_the_window() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 59 * 60_000 + 50_000, 5_272);
        set_clock(H13 + 59 * 60_000 + 59_000);
        assert!(
            source.roll_written_hours(&quiet()).is_empty(),
            "not over yet"
        );

        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        // Every monitored service measured the hour: gitea/db wrote 163_840 bytes between the two
        // captures; a service that wrote nothing measured a real 0.
        assert_eq!(posted.len(), 11, "{posted:?}");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .unwrap()
            .1;
        assert_eq!(db.bytes, 163_840);
        assert_eq!(db.megabytes(), 0.16);
        assert_eq!(db.comment(), "13:00–14:00 UTC; measured 0 of 60 min");
        let caddy = posted
            .iter()
            .find(|(key, _)| *key == key_of("caddy", "caddy"))
            .unwrap()
            .1;
        assert_eq!(caddy.bytes, 0);
        // Posted once: the next tick of the same hour has nothing to post.
        set_clock(H13 + 60 * 60_000 + 6_000);
        assert!(source.roll_written_hours(&quiet()).is_empty());
    }

    #[test]
    fn a_restart_mid_hour_continues_the_hour_from_the_state_file() {
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-written-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);

        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut before = garage_source(
                &collector,
                FixtureEngine::garage(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            before.clock = test_clock;
            two_rounds(&mut before, H13 + 30 * 60_000, 5_000);
            // The probe stops at 13:30:05: the running hour goes to the state file.
            Source::stop(&mut before, &quiet());
            collector.stop().expect("stop");
        }
        let collector = test_collector();
        collector.start().expect("start");
        assert!(std::fs::read_to_string(&path)
            .unwrap()
            .contains("\"written\""));

        // Back at 13:40 with the same containers; gitea/db wrote 1 MB while the probe was down.
        let mut engine = FixtureEngine::garage();
        add_written(&mut engine, 1, GITEA_DB, 1_000_000);
        let mut after = garage_source(
            &collector,
            engine,
            &DockerConfig::default(),
            Some(path.clone()),
        );
        after.clock = test_clock;
        let never = || false;
        set_clock(H13 + 40 * 60_000);
        after.engine.round = 1;
        after.poll(&quiet(), &never).expect("poll");
        after
            .sample_stats(&quiet(), &never)
            .expect("first round after the restart");

        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = after.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .unwrap()
            .1;
        // Before the stop (163_840) plus the downtime (1 MB): the hour is continued, not lost.
        assert_eq!(db.bytes, 163_840 + 1_000_000);
        assert_eq!(db.comment(), "13:00–14:00 UTC; measured 10 of 60 min");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_measured_hour_is_posted_after_a_restart_even_while_the_daemon_is_down() {
        let dir = std::env::temp_dir().join(format!(
            "hsm-probe-docker-written-down-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut before = garage_source(
                &collector,
                FixtureEngine::garage(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            before.clock = test_clock;
            two_rounds(&mut before, H13 + 50 * 60_000, 5_000);
            Source::stop(&mut before, &quiet());
            collector.stop().expect("stop");
        }
        // Restarted at 14:00:01 with the daemon not answering: nothing registered in prime, but
        // the measured 13:00 hour is still posted — its sensor is registered on demand.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        engine.down = true;
        let mut after = garage_source(&collector, engine, &DockerConfig::default(), Some(path));
        after.clock = test_clock;
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = after.roll_written_hours(&quiet());
        let paths = registered(&collector);
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .unwrap()
            .1;
        assert_eq!(db.bytes, 163_840);
        assert!(paths
            .iter()
            .any(|p| p == "Docker/gitea/db/Disk written per hour"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_idle_containers_first_burst_is_counted() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        let set = |engine: &mut FixtureEngine, round: usize, value: Option<u64>| {
            let stats = engine.rounds[round]
                .iter_mut()
                .find(|(id, _)| id.starts_with(GITEA_DB))
                .unwrap()
                .1;
            let entries = stats
                .blkio_stats
                .io_service_bytes_recursive
                .as_mut()
                .unwrap();
            match value {
                None => entries.clear(),
                Some(value) => {
                    entries.iter_mut().find(|e| e.op == "write").unwrap().value = value;
                }
            }
        };
        // gitea/db has done no block I/O yet (io.stat lists no device), then writes 4 KiB.
        set(&mut engine, 0, None);
        set(&mut engine, 1, Some(4_096));
        let mut source = garage_source(&collector, engine, &DockerConfig::default(), None);
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .expect("posted")
            .1;
        assert_eq!(
            db.bytes, 4_096,
            "the first write counts, it is not the baseline"
        );
    }

    #[test]
    fn a_host_without_write_counters_keeps_no_record_and_writes_no_state() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        for round in &mut engine.rounds {
            for stats in round.values_mut() {
                // Docker Desktop's shape.
                stats.blkio_stats.io_service_bytes_recursive = Some(Vec::new());
            }
        }
        let mut source = garage_source(&collector, engine, &DockerConfig::default(), None);
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        collector.stop().expect("stop");
        assert!(!source.written_dirty, "nothing to save");
        assert!(source
            .tracker
            .state
            .services
            .values()
            .all(|record| record.written.is_none()));
    }

    #[test]
    fn priming_stops_at_the_first_container_that_does_not_answer() {
        let collector = test_collector();
        let mut engine = FixtureEngine::garage();
        engine.wedged = Some("abbd59dcacc8");
        let mut source = garage_source(&collector, engine, &DockerConfig::default(), None);
        let db = source
            .engine
            .list
            .iter()
            .find(|c| c.id.starts_with("abbd59dcacc8"))
            .unwrap()
            .id
            .clone();
        // Three replicas that all time out: one timeout, not three.
        let (limit, writes) = source.first_stats_of(&[db.clone(), db.clone(), db]);
        assert_eq!((limit, writes), (None, false));
        assert_eq!(source.engine.stats_calls, 1);
    }

    #[test]
    fn a_container_restarted_while_the_probe_was_down_starts_a_new_baseline() {
        let dir = std::env::temp_dir().join(format!(
            "hsm-probe-docker-written-restarted-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut before = garage_source(
                &collector,
                FixtureEngine::garage(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            before.clock = test_clock;
            two_rounds(&mut before, H13 + 10 * 60_000, 5_000);
            Source::stop(&mut before, &quiet());
            collector.stop().expect("stop");
        }
        // gitea/db was restarted (same id, new StartedAt) and its new cgroup has already written
        // more than the old counter: a baseline, not a too-small delta.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::garage();
        add_written(&mut engine, 1, GITEA_DB, 1_000_000);
        let inspect = engine
            .inspects
            .iter_mut()
            .find(|(id, _)| id.starts_with(GITEA_DB))
            .unwrap()
            .1;
        inspect.state.started_at = "2026-09-29T13:15:00Z".into();
        let mut after = garage_source(&collector, engine, &DockerConfig::default(), Some(path));
        after.clock = test_clock;
        let never = || false;
        set_clock(H13 + 20 * 60_000);
        after.engine.round = 1;
        after.poll(&quiet(), &never).expect("poll");
        after
            .sample_stats(&quiet(), &never)
            .expect("round after the restart");
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = after.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .unwrap()
            .1;
        assert_eq!(
            db.bytes, 163_840,
            "only the delta measured before the restart"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_hour_that_ended_while_the_probe_was_down_is_dropped_not_posted_late() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 30 * 60_000, 5_000);
        // Next seen at 15:10: the 13:00 hour ended while the probe was not running.
        set_clock(H13 + 130 * 60_000);
        assert!(source.roll_written_hours(&quiet()).is_empty());
        collector.stop().expect("stop");
        let record = source.tracker.state.services[&key_of("gitea", "db")]
            .written
            .clone()
            .unwrap();
        assert_eq!(record.hour_start * 1000, H13 + 120 * 60_000);
        assert_eq!(record.bytes, 0);
    }

    #[test]
    fn a_recreated_container_keeps_the_service_hour() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = garage_source(
            &collector,
            FixtureEngine::garage(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        // gitea/db is recreated: the same service under a new id whose counter starts afresh.
        let old = source
            .engine
            .list
            .iter()
            .position(|c| c.id.starts_with(GITEA_DB))
            .unwrap();
        let new_id = format!("ffff{}", &source.engine.list[old].id[4..]);
        source.engine.list[old].id = new_id.clone();
        let inspect = source
            .engine
            .inspects
            .iter()
            .find(|(id, _)| id.starts_with(GITEA_DB))
            .unwrap()
            .1
            .clone();
        source.engine.inspects.insert(new_id.clone(), inspect);
        for round in 0..2 {
            let mut stats = source.engine.rounds[round]
                .iter()
                .find(|(id, _)| id.starts_with(GITEA_DB))
                .unwrap()
                .1
                .clone();
            let write = stats
                .blkio_stats
                .io_service_bytes_recursive
                .as_mut()
                .unwrap()
                .iter_mut()
                .find(|e| e.op == "write")
                .unwrap();
            write.value = 4_096 + 500 * round as u64;
            source.engine.rounds[round].insert(new_id.clone(), stats);
        }
        two_rounds(&mut source, H13 + 20 * 60_000, 5_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitea", "db"))
            .unwrap()
            .1;
        // Old container: 163_840. New one: its first sample is a baseline, then 500 bytes.
        assert_eq!(db.bytes, 163_840 + 500);
        let record = &source.tracker.state.services[&key_of("gitea", "db")];
        let baselines = &record.written.as_ref().unwrap().baselines;
        assert!(
            baselines.keys().all(|id| !id.starts_with(GITEA_DB)),
            "old id pruned"
        );
    }
}
