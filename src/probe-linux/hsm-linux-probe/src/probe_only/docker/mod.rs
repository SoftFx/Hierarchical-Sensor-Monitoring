//! The Docker Compose source (#1416): per-service CPU, memory, status, health, restarts, OOM
//! kills and block-device writes per hour under `<module>/Docker/<project>/<service>/…`, read from
//! the Docker Engine API over its Unix socket, plus the memory sensors of #1818 (own memory,
//! pressure, limit hits per hour, swap, in-container OOM kills) from the containers' cgroup v2
//! files ([`cgroup`]).
//!
//! A probe-only [`Source`] on a thread of its own with two cadences: stats every
//! `probe.docker.samplePeriodSec`, 60 s by default (CPU and memory samples into 5-minute bars,
//! write and limit-hit counters into the running hour — [`written`]; the cgroup files are read in
//! the same round) and, on the first tick and every 60 s after, a state poll
//! (listing + inspect: status, health, restart count, OOM). Every tick first closes a clock hour
//! that has ended and posts its `Disk written per hour` and `Memory limit hits per hour`, whether
//! or not the daemon answers.
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
pub mod cgroup;
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

use cgroup::{CgroupFs, CgroupMemory, OomKillTracker, PressureCounter, PressureTracker};
use engine::{ApiVersion, EngineApi, EngineError};
use identity::{Membership, Naming, ServiceKey};
use sensors::{CgroupSensors, ServiceSensors};
use state::{LoadOutcome, ServiceRecord, State};
use stats::{CpuCounters, CpuTracker};
use tracker::{ContainerObservation, InspectFacts, Tracker};
use written::{CompletedHour, WriteRecord};

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
    /// Tests only: the source's monotonic time, frozen at this offset from `origin` and moved on by
    /// the test, so the interval between two stats rounds is exactly what the test sets.
    #[cfg(test)]
    frozen_elapsed: Option<Duration>,
    /// When the next state poll is due (the first tick polls).
    next_poll: Instant,
    /// While the daemon is unreachable: no call before this.
    unavailable_until: Option<Instant>,
    backoff: Duration,
    /// A `Disk written per hour` or `Memory limit hits per hour` accumulator changed since the
    /// state file was last written.
    written_dirty: bool,
    /// When the state file was last written: the start of the tick that wrote it.
    saved_at: Instant,
    /// The start of the current tick (its `now`). Every save during the tick stamps `saved_at`
    /// with it, so the save deadline and the later ticks it is compared with share one reference,
    /// and the tick's own Engine API work does not eat into the half-period slack (#1507).
    tick_started: Instant,
    should_stop: fn() -> bool,
    /// The wall clock, Unix milliseconds (a seam for the hour-boundary tests).
    clock: fn() -> i64,
    /// Stacked block devices (LVM, dm-crypt, md), so a write is counted on the physical disks.
    stacking: written::Stacking,
    /// Each listed container's `State.StartedAt` from the last inspect: a change means a restart,
    /// whose write counter began again from 0.
    started_at: std::collections::HashMap<String, String>,
    /// Some container on this host has listed a block-I/O write counter since the probe started:
    /// the host accounts I/O per container, so a running container without one has written 0 so
    /// far (#1574). Never set on a host that lists none (Docker Desktop).
    counters_seen: bool,
    /// The containers' cgroup v2 directories (#1818): `/sys/fs/cgroup` in production.
    cgroups: CgroupFs,
    /// The last `memory.pressure` counter of every sampled container.
    pressure: PressureTracker,
    /// The last `memory.events` `oom_kill` count of every sampled container (in memory only).
    oom_kills: OomKillTracker,
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
            #[cfg(test)]
            frozen_elapsed: None,
            next_poll: Instant::now(),
            unavailable_until: None,
            backoff: config.sample_period(),
            written_dirty: false,
            saved_at: Instant::now(),
            tick_started: Instant::now(),
            should_stop: crate::shutdown::is_requested,
            clock: unix_now_ms,
            stacking: written::Stacking::default(),
            started_at: std::collections::HashMap::new(),
            counters_seen: false,
            cgroups: CgroupFs::default(),
            pressure: PressureTracker::default(),
            oom_kills: OomKillTracker::default(),
        }
    }

    /// The source's monotonic time, since `origin`: the clock of the CPU and pressure intervals.
    fn elapsed(&self) -> Duration {
        #[cfg(test)]
        {
            if let Some(at) = self.frozen_elapsed {
                return at;
            }
        }
        self.origin.elapsed()
    }

    /// One scheduled tick: the state poll when it is due, then a stats round. An unreachable daemon
    /// is logged once and backed off (doubling up to [`contract::MAX_BACKOFF`]); ticks inside the
    /// backoff make no call at all. Nothing is posted for a failed read.
    fn tick(&mut self, logger: &Logger) {
        let now = Instant::now();
        self.tick_started = now;
        // Hours close on the wall clock, whether or not the daemon answers.
        self.roll_hourly(logger);
        if self.unavailable_until.is_some_and(|until| now < until) {
            return;
        }
        let should_stop = self.should_stop;
        let mut failure = None;
        if now >= self.next_poll {
            failure = self.poll(logger, &should_stop).err();
            self.next_poll = on_tick_grid(now, contract::STATE_POLL_PERIOD, self.sample_period);
        }
        if failure.is_none() {
            failure = self.sample_stats(logger, &should_stop).err();
        }
        // The third tick-grid deadline: without the slack the save slips a tick (6 minutes at
        // the 60 s default).
        let save_due = on_tick_grid(
            self.saved_at,
            contract::WRITTEN_PERSIST_PERIOD,
            self.sample_period,
        );
        if self.written_dirty && now >= save_due {
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
                self.unavailable_until = Some(on_tick_grid(now, self.backoff, self.sample_period));
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
            // One stats read per running container, so `Memory used %` registers stating its limit,
            // `Disk written per hour` registers when the containers report writes, and the cgroup
            // memory sensors register where their files are.
            let first = if *has_run {
                roster
                    .get(key)
                    .map(|ids| self.first_stats_of(key, ids, logger))
                    .unwrap_or_default()
            } else {
                FirstStats::default()
            };
            let sensors = self.sensors_of(key);
            let mut problems = sensors.ensure_state_sensors(collector, true, *has_health);
            if *has_run {
                problems.extend(sensors.ensure_stats_sensors(collector, first.limit, first.writes));
                problems.extend(sensors.ensure_cgroup_sensors(collector, first.cgroup));
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
    /// when a read fails (the first stats round then sets it) — whether any container reports a
    /// block-device write counter, and which cgroup memory sensors its containers' files support.
    /// A failed read is logged (#1507): the service then registers without them, and they follow
    /// at the first stats round.
    fn first_stats_of(&mut self, key: &ServiceKey, ids: &[String], logger: &Logger) -> FirstStats {
        let mut readings = Vec::with_capacity(ids.len());
        let mut complete = true;
        let mut writes = false;
        let mut cgroup = CgroupSensors::default();
        let swap_seen = self.swap_seen(key);
        for (index, id) in ids.iter().enumerate() {
            let stats = match self.engine.stats(id) {
                Ok(stats) => stats,
                // A daemon or container that does not answer costs one timeout per service, not
                // one per replica, before Start; the first stats round catches up.
                Err(error) if error.is_unavailable() => {
                    logger.info(first_stats_failed(key, id, &error, index + 1 < ids.len()));
                    complete = false;
                    break;
                }
                Err(error) => {
                    logger.info(first_stats_failed(key, id, &error, false));
                    complete = false;
                    continue;
                }
            };
            writes |= written::written_bytes(&stats, &mut self.stacking, &mut self.counters_seen)
                .is_some();
            let reading = stats::memory_reading(&stats, self.host_mem_total);
            match reading {
                Some(reading) => readings.push(reading),
                None => complete = false,
            }
            if CpuCounters::from_stats(&stats, Duration::ZERO).is_some() {
                if let Some(memory) = self.read_cgroup(key, id, logger) {
                    let limited = reading.is_some_and(|reading| !reading.unlimited);
                    cgroup = cgroup.or(cgroup_sensors(&memory, limited, swap_seen));
                }
            }
        }
        let limit = complete
            .then(|| stats::service_memory(&readings, self.host_mem_total))
            .flatten()
            .map(|memory| (stats::limit_megabytes(memory.limit_bytes), memory.unlimited));
        FirstStats {
            limit,
            writes,
            cgroup,
        }
    }

    /// Whether the state remembers that a container of the service has used swap.
    fn swap_seen(&self, key: &ServiceKey) -> bool {
        self.tracker
            .state
            .services
            .get(key)
            .is_some_and(|record| record.swap_seen)
    }

    /// The memory files of a running container's cgroup, or `None` when it has no cgroup v2
    /// directory (cgroup v1, rootless Docker, Docker Desktop) — logged once per container: it then
    /// gets `Own memory` only.
    fn read_cgroup(&mut self, key: &ServiceKey, id: &str, logger: &Logger) -> Option<CgroupMemory> {
        let memory = self.cgroups.read(id);
        let once = format!("cgroup:{id}");
        match memory {
            Some(_) => {
                self.log_once.clear(&once);
            }
            None => {
                if self.log_once.raise(&once) {
                    logger.info(format!(
                        "docker: {key} container {}: no cgroup v2 directory \
                         (system.slice/docker-<id>.scope or docker/<id> under /sys/fs/cgroup; \
                         cgroup v1, rootless Docker or Docker Desktop?); it reports Own memory \
                         but no Memory pressure %, Memory limit hits per hour, Swap used or \
                         in-container OOM kills",
                        short_id(id)
                    ));
                }
            }
        }
        memory
    }

    /// Close every service's `Disk written per hour` and `Memory limit hits per hour` hour that has
    /// ended and post it. Only the hour that just ended is posted; an older one (the probe was down
    /// when it ended) is dropped with a log line — it would arrive hours late under the current
    /// time. The state file is written at once, so a restart cannot post the same hour twice.
    /// Returns the hours handed to the collector: (disk written, limit hits).
    fn roll_hourly(&mut self, logger: &Logger) -> (PostedHours, PostedHours) {
        let mut rolled = false;
        let written = self.roll_hours(logger, Hourly::DiskWritten, &mut rolled);
        let limit_hits = self.roll_hours(logger, Hourly::LimitHits, &mut rolled);
        if rolled {
            self.written_dirty = true;
            self.persist(logger);
        }
        (written, limit_hits)
    }

    #[cfg(test)]
    fn roll_written_hours(&mut self, logger: &Logger) -> PostedHours {
        self.roll_hourly(logger).0
    }

    /// [`DockerSource::roll_hourly`] for one of the two hourly sensors; `rolled` is set when an
    /// accumulator moved to a new hour (the state must be written).
    fn roll_hours(&mut self, logger: &Logger, kind: Hourly, rolled: &mut bool) -> PostedHours {
        let now_ms = (self.clock)();
        let current = written::hour_start(now_ms);
        let name = kind.name();
        let mut posted = Vec::new();
        for (key, record) in self.tracker.state.services.iter_mut() {
            let node = record.node.clone();
            let Some(accumulator) = kind.accumulator(record) else {
                continue;
            };
            if accumulator.hour_start >= current {
                continue;
            }
            *rolled = true;
            let Some(done) = accumulator.roll(now_ms) else {
                continue;
            };
            let node = node.unwrap_or_else(|| self.naming.node(key));
            if done.hour_start + 3600 != current {
                logger.info(format!(
                    "docker: {node}: {name} for {} not posted: it ended more than an hour ago \
                     (the probe was not running at the next hour boundary); only the hour that \
                     has just ended is posted after a restart",
                    done.comment()
                ));
                continue;
            }
            // A measured hour means the service fed this sensor, so it exists on the server; it
            // may just not be registered in this run yet (restarted while the daemon did not
            // answer): register it now rather than drop the hour.
            let sensors = self
                .sensors
                .entry(key.clone())
                .or_insert_with(|| ServiceSensors::new(node.clone(), self.sample_period));
            let problem = match kind {
                Hourly::DiskWritten => sensors.ensure_disk_written(self.collector),
                Hourly::LimitHits => sensors.ensure_limit_hits(self.collector),
            };
            if let Some(problem) = problem {
                if self.log_once.raise(&format!("register:{problem}")) {
                    logger.error(format!(
                        "docker: sensor {problem}; {name} for {} is lost",
                        done.comment()
                    ));
                }
                continue;
            }
            let comment = done.comment();
            let (result, shown) = match kind {
                Hourly::DiskWritten => (
                    sensors.disk_written.as_ref().map(|sensor| {
                        sensor.add_with(done.megabytes(), SensorStatus::Ok, Some(&comment))
                    }),
                    format!("{} MB", done.megabytes()),
                ),
                Hourly::LimitHits => (
                    sensors.limit_hits.as_ref().map(|sensor| {
                        sensor.add_with(done.count(), SensorStatus::Ok, Some(&comment))
                    }),
                    done.count().to_string(),
                ),
            };
            match result {
                Some(Ok(())) => {
                    logger.log(
                        Level::Debug,
                        &format!("docker: {node}: {name} {shown} ({comment})"),
                    );
                    posted.push((key.clone(), done));
                }
                Some(Err(error)) if self.log_once.raise(&format!("post:{node}:{name}")) => {
                    logger.error(format!("docker: {node}: value not posted: {name}: {error}"));
                }
                _ => {}
            }
        }
        posted
    }

    fn sensors_of(&mut self, key: &ServiceKey) -> &mut ServiceSensors<'c> {
        let node = self.naming.node(key);
        self.sensors
            .entry(key.clone())
            .or_insert_with(|| ServiceSensors::new(node, self.sample_period))
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
                .or_insert_with(|| ServiceSensors::new(node, self.sample_period));
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
            let ids = roster.get(key).map_or(&[][..], Vec::as_slice);
            for accumulator in [record.written.as_mut(), record.limit_hits.as_mut()]
                .into_iter()
                .flatten()
            {
                let before = accumulator.baselines.len();
                accumulator.retain(ids);
                self.written_dirty |= accumulator.baselines.len() != before;
            }
        }
        self.started_at.retain(|id, _| {
            roster
                .values()
                .any(|ids| ids.iter().any(|listed| listed == id))
        });
        self.roster = roster;
        let sampled = |id: &str| {
            self.roster
                .values()
                .any(|ids| ids.iter().any(|listed| listed == id))
        };
        self.cpu.retain(sampled);
        self.pressure.retain(sampled);
        self.oom_kills.retain(sampled);
        self.persist_if_dirty(logger);
        Ok(())
    }

    /// The stats tick: one one-shot stats call per sampled container, CPU and memory into bars;
    /// the cgroup memory files of each running container are read in the same round (#1818).
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
            let mut round = MemoryRound::new(self.swap_seen(key));

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
                        self.pressure.forget(id);
                        cpu_valid = false;
                        memory_valid = false;
                        round.unknown();
                        continue;
                    }
                };
                let now_ms = (self.clock)();
                // A running container that lists no write yet reads as 0 once the host has shown
                // that it accounts block I/O per container (#1574); see `written_bytes`.
                let counter =
                    written::written_bytes(&stats, &mut self.stacking, &mut self.counters_seen);
                let started_at = self.started_at.get(id).cloned();
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
                            match accumulator.sample_written(
                                id,
                                counter,
                                now_ms,
                                self.sample_period,
                                started_at.as_deref(),
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
                        started_at.as_deref().unwrap_or_default()
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
                let at = self.elapsed();
                let running = match CpuCounters::from_stats(&stats, at) {
                    Some(counters) => {
                        any_counters = true;
                        match self.cpu.sample(id, counters, self.sample_period) {
                            Ok(percent) => cpu_total += percent,
                            Err(_) => cpu_valid = false,
                        }
                        true
                    }
                    None => {
                        // Stopped between the poll and now; the next poll drops it.
                        self.cpu.forget(id);
                        cpu_valid = false;
                        false
                    }
                };
                let reading = stats::memory_reading(&stats, self.host_mem_total);
                match reading {
                    Some(reading) => readings.push(reading),
                    None => memory_valid = false,
                }
                match stats::own_memory_bytes(&stats) {
                    Some(bytes) => round.own_bytes = round.own_bytes.saturating_add(bytes),
                    None => round.own_valid = false,
                }
                if running {
                    let read = ContainerRead {
                        id,
                        started_at: started_at.as_deref(),
                        limited: reading.is_some_and(|reading| !reading.unlimited),
                        at,
                        now_ms,
                    };
                    self.sample_cgroup(key, &read, &mut round, logger);
                } else {
                    self.pressure.forget(id);
                    round.pressure_valid = false;
                    round.swap_valid = false;
                }
            }

            let now_ms = (self.clock)();
            if let Some(record) = self.tracker.state.services.get_mut(key) {
                if let (Some(gap), Some(accumulator)) = (written_gap, record.written.as_mut()) {
                    accumulator.cover(gap, now_ms);
                }
                if let (Some(gap), Some(accumulator)) =
                    (round.limit_gap, record.limit_hits.as_mut())
                {
                    accumulator.cover(gap, now_ms);
                }
            }
            if !any_counters {
                continue;
            }
            if round.swap_in_use {
                self.tracker.mark_swap_seen(key);
            }
            if round.oom_killed {
                self.tracker.latch_oom(key, unix_now(), self.oom_latch);
            }
            let node = self.naming.node(key);
            let sensors = self
                .sensors
                .entry(key.clone())
                .or_insert_with(|| ServiceSensors::new(node, self.sample_period));
            let memory = memory_valid
                .then(|| stats::service_memory(&readings, self.host_mem_total))
                .flatten();
            let limit = memory.map(|m| (stats::limit_megabytes(m.limit_bytes), m.unlimited));
            let mut problems = sensors.ensure_stats_sensors(self.collector, limit, any_writes);
            problems.extend(sensors.ensure_cgroup_sensors(self.collector, round.sensors));
            report_problems(logger, &mut self.log_once, &problems);

            let mut failures = Vec::new();
            logger.log(
                Level::Debug,
                &format!(
                    "docker: {}: CPU {}, memory {}, {}",
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
                    ),
                    round.describe()
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
            for (name, sensor, value) in [
                (
                    contract::OWN_MEMORY,
                    &sensors.own_memory,
                    round.own_valid.then(|| stats::megabytes(round.own_bytes)),
                ),
                (
                    contract::MEMORY_PRESSURE,
                    &sensors.memory_pressure,
                    round.pressure_valid.then_some(round.pressure_max),
                ),
                (
                    contract::SWAP_USED,
                    &sensors.swap_used,
                    round.swap_valid.then(|| stats::megabytes(round.swap_bytes)),
                ),
            ] {
                if let (Some(sensor), Some(value)) = (sensor, value) {
                    if let Err(error) = sensor.add(value) {
                        failures.push(format!("{name}: {error}"));
                    }
                }
            }
            if round.oom_killed {
                logger.info(format!(
                    "docker: {}: a process of a running container was killed for running out of \
                     memory (its cgroup's oom_kill grew); OOM killed is latched for {} h",
                    sensors.node,
                    self.oom_latch.as_secs() / 3600
                ));
                if let Some(sensor) = &sensors.oom_killed {
                    if let Err(error) = sensor.add(true) {
                        failures.push(format!("OOM killed: {error}"));
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

    /// Fold one running container's cgroup memory files into the service's round (#1818):
    /// pressure into the worst-replica percent, swap into the sum, `oom_kill` growth into the OOM
    /// latch and `max` into the running `Memory limit hits per hour` (only while the container has
    /// a memory limit). A container without a cgroup v2 directory leaves the pressure and swap of
    /// this round unknown.
    fn sample_cgroup(
        &mut self,
        key: &ServiceKey,
        read: &ContainerRead<'_>,
        round: &mut MemoryRound,
        logger: &Logger,
    ) {
        let id = read.id;
        let Some(memory) = self.read_cgroup(key, id, logger) else {
            self.pressure.forget(id);
            self.oom_kills.forget(id);
            round.pressure_valid = false;
            round.swap_valid = false;
            return;
        };
        round.sensors = round
            .sensors
            .or(cgroup_sensors(&memory, read.limited, round.swap_seen));
        match memory.pressure_some_us {
            Some(total_us) => {
                let counter = PressureCounter {
                    total_us,
                    at: read.at,
                    started_at: read.started_at.map(str::to_string),
                };
                match self.pressure.sample(id, counter, self.sample_period) {
                    Ok(percent) => round.pressure_max = round.pressure_max.max(percent),
                    Err(_) => round.pressure_valid = false,
                }
            }
            None => {
                self.pressure.forget(id);
                round.pressure_valid = false;
            }
        }
        match memory.swap_bytes {
            Some(bytes) => {
                round.swap_bytes = round.swap_bytes.saturating_add(bytes);
                round.swap_in_use |= bytes > 0;
            }
            None => round.swap_valid = false,
        }
        let events = memory.events.unwrap_or_default();
        if let Some(kills) = events.oom_kill {
            round.oom_killed |= self.oom_kills.grew(id, kills, read.started_at);
        }
        let (Some(hits), true) = (events.max, read.limited) else {
            return;
        };
        if let Some(record) = self.tracker.state.services.get_mut(key) {
            let accumulator = record
                .limit_hits
                .get_or_insert_with(|| WriteRecord::new(read.now_ms));
            self.written_dirty = true;
            if let Ok((_, gap)) =
                accumulator.sample(id, hits, read.now_ms, self.sample_period, read.started_at)
            {
                round.limit_gap = Some(round.limit_gap.unwrap_or(0).max(gap));
            }
        }
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
        self.saved_at = self.tick_started;
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

/// What the priming stats read of a service's containers tells its registration.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct FirstStats {
    /// The memory limit (MB, unlimited) for the `Memory used %` description.
    limit: Option<(i32, bool)>,
    /// A container reports a block-device write counter (`Disk written per hour`).
    writes: bool,
    /// The cgroup memory sensors its containers' files support.
    cgroup: CgroupSensors,
}

/// The cgroup memory sensors one running container can feed (#1818), the registration rules in
/// one place: `Memory pressure %` needs a readable `memory.pressure`; `Memory limit hits per hour`
/// a readable `max` in `memory.events` **and** a memory limit; `Swap used` a readable
/// `memory.swap.current` and swap in use now or at some point before (`swap_seen`, from the
/// state). A container without a cgroup v2 directory feeds none of them.
fn cgroup_sensors(memory: &CgroupMemory, limited: bool, swap_seen: bool) -> CgroupSensors {
    CgroupSensors {
        pressure: memory.pressure_some_us.is_some(),
        limit_hits: limited && memory.events.is_some_and(|events| events.max.is_some()),
        swap: memory
            .swap_bytes
            .is_some_and(|bytes| bytes > 0 || swap_seen),
    }
}

/// One container of a stats round, as its cgroup sampling needs it.
struct ContainerRead<'a> {
    id: &'a str,
    /// `State.StartedAt` from the last inspect.
    started_at: Option<&'a str>,
    /// The container has a memory limit of its own.
    limited: bool,
    /// Monotonic time of the read (the CPU stamp).
    at: Duration,
    /// Wall clock of the read, Unix ms (the hourly accumulators).
    now_ms: i64,
}

/// A service's #1818 memory figures of one stats round, its replicas folded in as they are read.
/// A figure some replica could not give is not posted this round (`*_valid`), never guessed.
#[derive(Debug)]
struct MemoryRound {
    own_bytes: u64,
    own_valid: bool,
    /// The worst replica's `Memory pressure %`.
    pressure_max: f64,
    pressure_valid: bool,
    swap_bytes: u64,
    swap_valid: bool,
    /// Some replica has swap in use now.
    swap_in_use: bool,
    /// The state remembers swap in use before.
    swap_seen: bool,
    /// The longest interval among this round's accepted limit-hit deltas.
    limit_gap: Option<u64>,
    /// Some replica's `oom_kill` grew.
    oom_killed: bool,
    /// The cgroup sensors the replicas' files support.
    sensors: CgroupSensors,
}

impl MemoryRound {
    fn new(swap_seen: bool) -> Self {
        Self {
            own_bytes: 0,
            own_valid: true,
            pressure_max: 0.0,
            pressure_valid: true,
            swap_bytes: 0,
            swap_valid: true,
            swap_in_use: false,
            swap_seen,
            limit_gap: None,
            oom_killed: false,
            sensors: CgroupSensors::default(),
        }
    }

    /// A replica's stats could not be read: none of the round's memory figures is complete.
    fn unknown(&mut self) {
        self.own_valid = false;
        self.pressure_valid = false;
        self.swap_valid = false;
    }

    /// For the round's debug line.
    fn describe(&self) -> String {
        let figure = |valid: bool, text: String| if valid { text } else { "skipped".into() };
        format!(
            "own memory {}, pressure {}, swap {}",
            figure(
                self.own_valid,
                format!("{:.2} MB", stats::megabytes(self.own_bytes))
            ),
            figure(
                self.pressure_valid && self.sensors.pressure,
                format!("{:.2} %", self.pressure_max)
            ),
            figure(
                self.swap_valid,
                format!("{:.2} MB", stats::megabytes(self.swap_bytes))
            ),
        )
    }
}

/// The hours an hourly sensor handed to the collector, per service.
type PostedHours = Vec<(ServiceKey, CompletedHour)>;

/// The two hourly sensors: the same accumulator ([`WriteRecord`]) and posting rules over different
/// counters.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum Hourly {
    DiskWritten,
    LimitHits,
}

impl Hourly {
    fn name(self) -> &'static str {
        match self {
            Hourly::DiskWritten => contract::DISK_WRITTEN,
            Hourly::LimitHits => contract::MEMORY_LIMIT_HITS,
        }
    }

    fn accumulator(self, record: &mut ServiceRecord) -> Option<&mut WriteRecord> {
        match self {
            Hourly::DiskWritten => record.written.as_mut(),
            Hourly::LimitHits => record.limit_hits.as_mut(),
        }
    }
}

/// The priming stats read of one container failed (#1507): name the service, the container and
/// the error, so a Start batch short of a few sensors can be explained from the journal.
fn first_stats_failed(key: &ServiceKey, id: &str, error: &EngineError, skips_rest: bool) -> String {
    let rest = if skips_rest {
        " (its other replicas are not asked either)"
    } else {
        ""
    };
    format!(
        "docker: priming stats of {key} container {} failed ({error}){rest}; it registers \
         without the stats-based details (the memory limit, Disk written per hour), which follow \
         at the first stats round",
        short_id(id)
    )
}

fn report_problems(logger: &Logger, log_once: &mut LogOnce, problems: &[String]) {
    for problem in problems {
        if log_once.raise(&format!("register:{problem}")) {
            logger.error(format!("docker: sensor {problem}"));
        }
    }
}

/// The deadline for "`wait` after the tick that started at `from`": the tick on the grid nearest
/// to `from + wait` meets it. Ticks come on a fixed-rate grid (`run_source`) but each starts a
/// little late (wake-up latency), and by a different amount each time, so a deadline of exactly
/// `from + wait` is missed whenever the later tick starts earlier within its slot than this one
/// did, and the action slips by a whole period: at the 60 s default the state poll would run every
/// 2 minutes and the retry after an outage would wait 2 minutes. Half a period of slack absorbs
/// that jitter (a 5 s period still polls every 60 s, a 7 s one every 63 s).
fn on_tick_grid(from: Instant, wait: Duration, period: Duration) -> Instant {
    from + wait.saturating_sub(period / 2)
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
        // The running hours' accumulators, so a restart continues them.
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
    // The cgroup v2 mount, read-only under the unit's ProtectControlGroups=yes.
    source.cgroups = CgroupFs::new(Some(sys_root.join("fs/cgroup")));
    source.stacking = written::Stacking::new(Some(sys_root));
    if let Some(note) = sample_period_note(config) {
        logger.info(note);
    }
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

/// One start-up line when `probe.docker.samplePeriodSec` samples faster than the default: a config
/// seeded by a probe before 0.8.1 pins the old 5 s (the skeleton wrote it out, and an upgrade never
/// edits the config), so the owner's once-a-minute decision does not reach that host by itself.
fn sample_period_note(config: &DockerConfig) -> Option<String> {
    let default = contract::DEFAULT_SAMPLE_PERIOD.as_secs();
    (config.sample_period_sec < default).then(|| {
        format!(
            "docker: probe.docker.samplePeriodSec is {} s in the config, below the {default} s \
             default since probe 0.8.1, so Docker stats are sampled every {} s; to sample once a \
             minute remove the key (or set {default}) and restart — see the README \
             \"Upgrade note (0.8.1)\"",
            config.sample_period_sec, config.sample_period_sec
        )
    })
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

    /// Serves the demo captures: the listing, every inspect, and the two stats rounds (5.3 s
    /// apart on the host, taken at the old 5 s cadence) in turn.
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
        /// When the latest stats call was made, for the save-stamp test.
        pub last_stats_at: Option<Instant>,
    }

    impl FixtureEngine {
        pub fn demo() -> Self {
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
                last_stats_at: None,
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
            self.last_stats_at = Some(Instant::now());
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
        options.module = Some(crate::config::DEFAULT_MODULE.into());
        Collector::new(&options).expect("create")
    }

    fn quiet() -> Logger {
        Logger::new(Level::Error, None)
    }

    /// A source over the demo captures whose stop check never fires (the process-global
    /// shutdown flag is set by another unit test).
    pub(crate) fn demo_source<'c>(
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
            Some(DEMO_MEM_TOTAL),
        );
        source.should_stop = || false;
        // Its monotonic time stands still until a test moves it on: an interval between two rounds
        // is what the test sets, not that plus however long the test took.
        source.frozen_elapsed = Some(Duration::ZERO);
        source
    }

    /// Moves the frozen monotonic time of a demo source on by `by`.
    fn advance_monotonic(source: &mut DockerSource<'_, FixtureEngine>, by: Duration) {
        let at = source
            .frozen_elapsed
            .expect("a demo source runs on a frozen monotonic time");
        source.frozen_elapsed = Some(at + by);
    }

    const DEMO_MEM_TOTAL: u64 = 16_298_872 * 1024;

    /// A fake cgroup v2 mount holding the demo containers' first-round cgroup files, which
    /// `source` reads from now on; `cgroup::tests::demo_cgroups(&tree, "cgroup", 1)` moves it to
    /// the second round. Keep the tree alive while the source samples.
    fn demo_cgroup_mount(
        source: &mut DockerSource<'_, FixtureEngine>,
    ) -> crate::probe_only::host::tests::FakeTree {
        let tree = crate::probe_only::host::tests::FakeTree::new("docker-cgroup");
        cgroup::tests::demo_cgroups(&tree, "cgroup", 0);
        source.cgroups = CgroupFs::new(Some(tree.0.join("cgroup")));
        tree
    }

    /// Drive the demo fixtures through one poll and two stats rounds, as the running source
    /// would over its first minute.
    pub(crate) fn drive_demo(collector: &Collector) {
        drive_demo_on(collector, |_| {});
    }

    /// `drive_demo`, then `more` on the same source.
    fn drive_demo_on(
        collector: &Collector,
        more: impl FnOnce(&mut DockerSource<'_, FixtureEngine>),
    ) {
        let mut source = demo_source(
            collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        let cgroups = demo_cgroup_mount(&mut source);
        let never = || false;
        source.poll(&quiet(), &never).expect("poll");
        source
            .sample_stats(&quiet(), &never)
            .expect("first stats round");
        source.engine.round = 1;
        cgroup::tests::demo_cgroups(&cgroups, "cgroup", 1);
        // One default sample period on the source's monotonic clock, so the CPU interval check
        // passes. The captures are really 5.3 s apart, but the percent is the ratio of the two
        // counters' deltas, whatever the interval.
        advance_monotonic(&mut source, contract::DEFAULT_SAMPLE_PERIOD);
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
    fn the_demo_tree_has_no_empty_nodes() {
        let collector = test_collector();
        collector.start().expect("start");
        drive_demo(&collector);
        let paths = registered(&collector);
        collector.stop().expect("stop");

        let docker: Vec<&String> = paths
            .iter()
            .filter(|p| p.starts_with(".probe/Docker/"))
            .collect();
        // 11 services × 3 state sensors + 4 healthchecks + 11 × 4 stats sensors (CPU, Memory used %,
        // Own memory, Disk written per hour) + the cgroup ones (#1818): Memory pressure % for the 9
        // services with a readable memory.pressure (not panelhost: no cgroup directory; not mdb: no
        // PSI), Memory limit hits per hour for the 10 with memory.events (all have a limit), Swap
        // used for hsm/app only (the one that swaps). The twelfth container, lumina-ci/ci-image,
        // is a completed one-shot job and not monitored at all.
        assert_eq!(
            docker.len(),
            11 * 3 + 4 + 11 * 4 + 9 + 10 + 1,
            "{docker:#?}"
        );
        assert!(paths.iter().all(|p| !p.ends_with("/Memory limit")));
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(has(".probe/Docker/gitsrv/db/Service status"));
        assert!(has(".probe/Docker/gitsrv/db/Health"));
        assert!(has(".probe/Docker/gitsrv/db/CPU"));
        // Own memory comes from the Engine API stats: every service has it, panelhost included.
        assert!(has(".probe/Docker/panelhost/panelhost/Own memory"));
        // No cgroup v2 directory: Own memory only.
        for sensor in [
            "Memory pressure %",
            "Memory limit hits per hour",
            "Swap used",
        ] {
            assert!(!has(&format!(".probe/Docker/panelhost/panelhost/{sensor}")));
        }
        // No PSI: no pressure node, the rest is there.
        assert!(!has(".probe/Docker/lumina/mdb/Memory pressure %"));
        assert!(has(".probe/Docker/lumina/mdb/Memory limit hits per hour"));
        // Swap only where swap was used: hsm/app swaps, gitsrv/db (0 bytes) does not.
        assert!(has(".probe/Docker/hsm/app/Swap used"));
        assert!(!has(".probe/Docker/gitsrv/db/Swap used"));
        // The cgroupfs driver's layout (cproxy) is found like the systemd one.
        assert!(has(".probe/Docker/cproxy/cproxy/Memory pressure %"));
        // The exited one-shot image build (Exited (0), restart "no"): a completed job, no node.
        assert!(paths
            .iter()
            .all(|p| !p.starts_with(".probe/Docker/lumina-ci/ci-image/")));
        // No healthcheck, no Health node.
        assert!(!has(".probe/Docker/hsm/app/Health"));
    }

    #[test]
    fn an_unreachable_daemon_backs_off_and_posts_nothing() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        engine.down = true;
        // A 5 s period, so the doubling is visible under the 60 s cap.
        let config = DockerConfig {
            sample_period_sec: 5,
            ..DockerConfig::default()
        };
        let mut source = demo_source(&collector, engine, &config, None);
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
            .find(|json| json.contains(".probe/Docker/gitsrv/db/Service status"))
            .expect("gitsrv/db registered at runtime");
        assert!(status.contains("$operation Running"), "{status}");
    }

    /// Ticks start a little late on their fixed-rate grid, each by a different amount. A deadline
    /// "60 s after this tick" must still be met by the tick one minute later when that one starts
    /// earlier within its slot — otherwise, at the 60 s default, the state poll and the retry after
    /// an outage would slip to every 2 minutes.
    #[test]
    fn at_the_default_period_the_state_poll_and_the_retry_run_every_tick() {
        let period = contract::DEFAULT_SAMPLE_PERIOD;
        assert_eq!(period, contract::STATE_POLL_PERIOD);
        let jitter = Duration::from_millis(1);
        let tick = Instant::now() + Duration::from_secs(1);
        let deadline = on_tick_grid(tick, contract::STATE_POLL_PERIOD, period);
        assert!(tick < deadline, "not due again in the same tick");
        assert!(
            tick + period - jitter >= deadline,
            "the next tick polls although it started earlier in its slot"
        );
        // A 5 s period keeps polling every 60 s: the 11th tick does not, the 12th does.
        let five = Duration::from_secs(5);
        let deadline = on_tick_grid(tick, contract::STATE_POLL_PERIOD, five);
        assert!(tick + five * 11 + jitter < deadline);
        assert!(tick + five * 12 - jitter >= deadline);

        // The source, at the default period: a tick that polled is due again on the next tick.
        let collector = test_collector();
        collector.start().expect("start");
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-grid-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            Some(dir.join(state::STATE_FILE_NAME)),
        );
        // Mid-hour on a fixed wall clock: no hour rolls (and saves) between the ticks below.
        source.clock = || 1_800_001_800_000;
        Source::sample(&mut source, &quiet());
        assert!(source.next_poll <= Instant::now() + period / 2);
        // An outage: the backoff stays one period (the 60 s cap) and the next tick retries.
        source.engine.down = true;
        source.next_poll = Instant::now();
        Source::sample(&mut source, &quiet());
        assert_eq!(source.backoff, period);
        let until = source.unavailable_until.expect("backing off");
        assert!(until <= Instant::now() + period / 2);

        // The state-file save, every 5 minutes. `saved_at` is stamped with the saving tick's start
        // (#1507), the reference the later ticks compare with, so on the fifth tick after it
        // slightly less than 5 minutes have passed: that tick saves. The fourth does not (not a
        // tick early).
        source.engine.down = false;
        source.unavailable_until = None;
        let ago = |elapsed: Duration| Instant::now().checked_sub(elapsed).expect("clock");
        source.written_dirty = true;
        let fourth = ago(contract::WRITTEN_PERSIST_PERIOD - period + Duration::from_millis(1));
        source.saved_at = fourth;
        Source::sample(&mut source, &quiet());
        assert!(source.written_dirty, "not saved a tick early");
        assert_eq!(source.saved_at, fourth);
        let fifth = ago(contract::WRITTEN_PERSIST_PERIOD - Duration::from_millis(1));
        source.saved_at = fifth;
        source.engine.last_stats_at = None;
        Source::sample(&mut source, &quiet());
        assert!(!source.written_dirty, "saved on this tick, not the next");
        assert!(source.saved_at > fifth);
        // Stamped with the tick's start, before its Engine API work, not after it.
        assert_eq!(source.saved_at, source.tick_started);
        let stats_at = source.engine.last_stats_at.expect("the tick sampled stats");
        assert!(
            source.saved_at <= stats_at,
            "stamped before the tick's stats calls"
        );
        collector.stop().expect("stop");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_config_sampling_faster_than_the_default_says_so_once_at_start() {
        assert_eq!(sample_period_note(&DockerConfig::default()), None);
        let slow = DockerConfig {
            sample_period_sec: 120,
            ..DockerConfig::default()
        };
        assert_eq!(sample_period_note(&slow), None);
        let old = DockerConfig {
            sample_period_sec: 5,
            ..DockerConfig::default()
        };
        assert_eq!(
            sample_period_note(&old).as_deref(),
            Some(
                "docker: probe.docker.samplePeriodSec is 5 s in the config, below the 60 s \
                 default since probe 0.8.1, so Docker stats are sampled every 5 s; to sample once \
                 a minute remove the key (or set 60) and restart — see the README \
                 \"Upgrade note (0.8.1)\""
            )
        );
    }

    /// The demo tree primed before Start: everything registers in the Start batch.
    #[test]
    fn prime_registers_the_whole_tree_before_start_with_alerts() {
        let collector = test_collector();
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        let _cgroups = demo_cgroup_mount(&mut source);
        assert_eq!(source.prime(&quiet()).expect("prime"), 11);
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        let docker: Vec<&String> = registrations
            .iter()
            .filter(|j| j.contains("\"Path\":\".probe/Docker/"))
            .collect();
        assert_eq!(docker.len(), 101);

        let find = |path: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("\"Path\":\".probe/Docker/{path}\"")))
                .unwrap_or_else(|| panic!("{path} not registered"))
                .clone()
        };
        // Service status: the Windows ServiceStatusPrototype alert, byte for byte (the same text
        // the native collector's own service-status registration golden pins).
        let status = find("gitsrv/db/Service status");
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
            "gitsrv/db/CPU",
            "gitsrv/db/Memory used %",
            "gitsrv/db/Health",
            "gitsrv/db/Restart count",
            "gitsrv/db/OOM killed",
        ] {
            assert!(
                find(path).contains("\"Alerts\":[{"),
                "{path} carries its alert"
            );
        }
        // The limit lives in the Memory used % description (gitsrv/db: 1 GiB).
        assert!(
            find("gitsrv/db/Memory used %").contains("**1024 MB** on this host"),
            "{}",
            find("gitsrv/db/Memory used %")
        );
        // Disk written per hour: decimal MB, EMA statistics, no alert, and the one-hour offset stated.
        let written = find("gitsrv/db/Disk written per hour");
        assert!(written.contains("\"OriginalUnit\":3"), "{written}");
        assert!(written.contains("\"Statistics\":1"), "{written}");
        assert!(!written.contains("\"Alerts\":[{"), "{written}");
        assert!(
            written.contains("sent just after the hour it covers"),
            "{written}"
        );
        // The #1818 memory sensors: bars in MB or %, a per-hour count, and no alert on any.
        for (path, unit) in [
            ("hsm/app/Own memory", "\"OriginalUnit\":3"),
            ("hsm/app/Memory pressure %", "\"OriginalUnit\":100"),
            (
                "hsm/app/Memory limit hits per hour",
                "\"OriginalUnit\":1100",
            ),
            ("hsm/app/Swap used", "\"OriginalUnit\":3"),
        ] {
            let json = find(path);
            assert!(json.contains(unit), "{path}: {json}");
            assert!(!json.contains("\"Alerts\":[{"), "{path}: {json}");
        }
        assert!(find("hsm/app/Memory limit hits per hour").contains("sent just after the hour"));
        assert!(find("hsm/app/OOM killed").contains("`oom_kill` counter"));
        // The state sensors store only changes on the server.
        for path in [
            "gitsrv/db/Service status",
            "gitsrv/db/Health",
            "gitsrv/db/OOM killed",
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
        let mut engine = FixtureEngine::demo();
        engine
            .list
            .retain(|c| c.label(identity::SERVICE_LABEL) != Some("panelhost"));
        let mut state = State::default();
        let key = ServiceKey::new("panelhost", "panelhost");
        let mut record = state::ServiceRecord::new(&key);
        record.last_seen = unix_now() - 3_600;
        state.services.insert(key.clone(), record);

        let dir = std::env::temp_dir().join(format!("hsm-probe-docker-src-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let mut source = demo_source(
            &collector,
            engine,
            &DockerConfig::default(),
            Some(path.clone()),
        );
        source.poll(&quiet(), &|| false).expect("poll");
        let panelhost: Vec<String> = registered(&collector)
            .into_iter()
            .filter(|p| p.starts_with(".probe/Docker/panelhost/"))
            .collect();
        assert_eq!(
            panelhost,
            vec![".probe/Docker/panelhost/panelhost/Service status"]
        );
        // The poll persisted the other services it met: 10 listed services (ci-image is a
        // completed job) plus the remembered panelhost.
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
            let mut engine = FixtureEngine::demo();
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
            let mut source = demo_source(&collector, engine, &config, None);
            source.poll(&quiet(), &|| false).expect("poll");
            let standalone = registered(&collector)
                .into_iter()
                .filter(|p| p.starts_with(".probe/Docker/_standalone/adhoc/"))
                .count();
            assert_eq!(standalone, expected, "composeOnly={compose_only}");
        }
    }

    #[test]
    fn a_wedged_container_does_not_blank_the_others() {
        // gitsrv-db's inspect and stats time out; every other service still reports, and db
        // keeps its status (from the listing) but posts no guessed health or stats.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        engine.wedged = Some("abbd59dcacc8");
        let mut source = demo_source(&collector, engine, &DockerConfig::default(), None);
        source
            .poll(&quiet(), &|| false)
            .expect("a timeout on one container is not an outage");
        source
            .sample_stats(&quiet(), &|| false)
            .expect("nor in the stats tick");
        let paths = registered(&collector);
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(has(".probe/Docker/gitsrv/db/Service status"));
        assert!(!has(".probe/Docker/gitsrv/db/Health"));
        assert!(!has(".probe/Docker/gitsrv/db/CPU"));
        assert!(has(".probe/Docker/gitsrv/gitsrv/Health"));
        assert!(has(".probe/Docker/gitsrv/gitsrv/CPU"));
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
            let mut source = demo_source(
                &collector,
                FixtureEngine::demo(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            source.poll(&quiet(), &|| false).expect("poll");
        }
        let (saved, _) = State::load(&path);
        let db = &saved.services[&ServiceKey::new("gitsrv", "db")];
        assert_eq!(db.node.as_deref(), Some("Docker/gitsrv/db"));

        // A restarted source adopts every remembered node before naming anything.
        let collector = test_collector();
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            Some(path.clone()),
        );
        assert_eq!(
            source.naming.node(&ServiceKey::new("gitsrv", "db")),
            "Docker/gitsrv/db"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn excluded_services_are_not_monitored_and_leave_the_state() {
        let collector = test_collector();
        collector.start().expect("start");
        // panelhost was monitored before (it is in the state); now both it and janitor are excluded.
        let mut state = State::default();
        let panelhost = ServiceKey::new("panelhost", "panelhost");
        let mut record = state::ServiceRecord::new(&panelhost);
        record.last_seen = unix_now() - 60;
        state.services.insert(panelhost.clone(), record);
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-exclude-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let config = DockerConfig {
            exclude: vec!["panelhost/*".into(), "lumina-ci/janitor".into()],
            ..DockerConfig::default()
        };
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &config,
            Some(path.clone()),
        );
        assert!(
            !source.tracker().state.services.contains_key(&panelhost),
            "dropped from the state at start: no Stopped, no alerts"
        );
        source.poll(&quiet(), &|| false).expect("poll");
        source.sample_stats(&quiet(), &|| false).expect("stats");
        let paths = registered(&collector);
        assert!(paths
            .iter()
            .all(|p| !p.starts_with(".probe/Docker/panelhost/")));
        assert!(paths
            .iter()
            .all(|p| !p.starts_with(".probe/Docker/lumina-ci/janitor/")));
        assert!(paths
            .iter()
            .any(|p| p.starts_with(".probe/Docker/lumina-ci/runner-heavy/")));
        let (saved, _) = State::load(&path);
        assert!(!saved.services.contains_key(&panelhost));
        assert!(!saved
            .services
            .contains_key(&ServiceKey::new("lumina-ci", "janitor")));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_changed_memory_limit_rewrites_the_memory_used_description() {
        let collector = test_collector();
        collector.start().expect("start");
        drive_demo_on(&collector, |source| {
            // hsm/app's container is recreated with mem_limit 512m.
            for round in &mut source.engine.rounds {
                for (id, stats) in round.iter_mut() {
                    if id.starts_with("c5abbadb3674") {
                        stats.memory_stats.limit = Some(512 * 1024 * 1024);
                    }
                }
            }
            advance_monotonic(source, contract::DEFAULT_SAMPLE_PERIOD);
            source
                .sample_stats(&quiet(), &|| false)
                .expect("third round");
        });
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        let app: Vec<&String> = registrations
            .iter()
            .filter(|json| json.contains(".probe/Docker/hsm/app/Memory used %"))
            .collect();
        assert_eq!(app.len(), 1, "re-registered in place, not added");
        assert!(app[0].contains("**512 MB** on this host"), "{}", app[0]);
    }

    const CI_IMAGE: &str = ".probe/Docker/lumina-ci/ci-image/Service status";

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
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        source.poll(&quiet(), &|| false).expect("poll");
        assert!(!registered(&collector).iter().any(|p| p == CI_IMAGE));
        let job = ServiceKey::new("lumina-ci", "ci-image");
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
        let job = ServiceKey::new("lumina-ci", "ci-image");
        let mut record = state::ServiceRecord::new(&job);
        record.last_seen = unix_now() - 60;
        state.services.insert(job, record);
        let dir = std::env::temp_dir().join(format!("hsm-probe-docker-job-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        state.save(&path).unwrap();

        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
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
    const GITSRV_DB: &str = "abbd59dcacc8";

    fn key_of(project: &str, service: &str) -> ServiceKey {
        ServiceKey::new(project, service)
    }

    /// Poll, then the two demo stats rounds `apart_ms` apart starting at `at_ms`.
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
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
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
        // Every monitored service measured the hour: gitsrv/db wrote 163_840 bytes between the two
        // captures; a service that wrote nothing measured a real 0.
        assert_eq!(posted.len(), 11, "{posted:?}");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
            .unwrap()
            .1;
        assert_eq!(db.bytes, 163_840);
        assert_eq!(db.megabytes(), 0.16);
        assert_eq!(db.comment(), "13:00–14:00 UTC; measured 0 of 60 min");
        let cproxy = posted
            .iter()
            .find(|(key, _)| *key == key_of("cproxy", "cproxy"))
            .unwrap()
            .1;
        assert_eq!(cproxy.bytes, 0);
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
            let mut before = demo_source(
                &collector,
                FixtureEngine::demo(),
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

        // Back at 13:40 with the same containers; gitsrv/db wrote 1 MB while the probe was down.
        let mut engine = FixtureEngine::demo();
        add_written(&mut engine, 1, GITSRV_DB, 1_000_000);
        let mut after = demo_source(
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
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
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
            let mut before = demo_source(
                &collector,
                FixtureEngine::demo(),
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
        let mut engine = FixtureEngine::demo();
        engine.down = true;
        let mut after = demo_source(&collector, engine, &DockerConfig::default(), Some(path));
        after.clock = test_clock;
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = after.roll_written_hours(&quiet());
        let paths = registered(&collector);
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
            .unwrap()
            .1;
        assert_eq!(db.bytes, 163_840);
        assert!(paths
            .iter()
            .any(|p| p == ".probe/Docker/gitsrv/db/Disk written per hour"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_idle_containers_first_burst_is_counted() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        let set = |engine: &mut FixtureEngine, round: usize, value: Option<u64>| {
            let stats = engine.rounds[round]
                .iter_mut()
                .find(|(id, _)| id.starts_with(GITSRV_DB))
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
        // gitsrv/db has done no block I/O yet (io.stat lists no device), then writes 4 KiB.
        set(&mut engine, 0, None);
        set(&mut engine, 1, Some(4_096));
        let mut source = demo_source(&collector, engine, &DockerConfig::default(), None);
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
            .expect("posted")
            .1;
        assert_eq!(
            db.bytes, 4_096,
            "the first write counts, it is not the baseline"
        );
    }

    #[test]
    fn an_idle_container_posts_0_like_its_neighbours() {
        // #1574: gitsrv/db recreated and idle — io.stat lists no device, `[]` in every round — next
        // to services that list a counter: its hour is a measured 0, posted, with its sensor.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        for round in &mut engine.rounds {
            let stats = round
                .iter_mut()
                .find(|(id, _)| id.starts_with(GITSRV_DB))
                .unwrap()
                .1;
            stats.blkio_stats.io_service_bytes_recursive = Some(Vec::new());
        }
        let mut source = demo_source(&collector, engine, &DockerConfig::default(), None);
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        let paths = registered(&collector);
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
            .expect("an idle hour is posted, not skipped")
            .1;
        assert_eq!(db.bytes, 0);
        assert!(paths
            .iter()
            .any(|p| p == ".probe/Docker/gitsrv/db/Disk written per hour"));
    }

    #[test]
    fn a_host_without_write_counters_keeps_no_record_and_writes_no_state() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        for round in &mut engine.rounds {
            for stats in round.values_mut() {
                // Docker Desktop's shape.
                stats.blkio_stats.io_service_bytes_recursive = Some(Vec::new());
            }
        }
        let mut source = demo_source(&collector, engine, &DockerConfig::default(), None);
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
        let mut engine = FixtureEngine::demo();
        engine.wedged = Some("abbd59dcacc8");
        let mut source = demo_source(&collector, engine, &DockerConfig::default(), None);
        let db = source
            .engine
            .list
            .iter()
            .find(|c| c.id.starts_with("abbd59dcacc8"))
            .unwrap()
            .id
            .clone();
        // Three replicas that all time out: one timeout, not three.
        let key = ServiceKey::new("gitsrv", "db");
        let lines = std::sync::Arc::new(std::sync::Mutex::new(Vec::new()));
        let sink = std::sync::Arc::clone(&lines);
        let logger = Logger::with_sink(Level::Debug, move |line: &str| {
            sink.lock().unwrap().push(line.to_string())
        });
        let first = source.first_stats_of(&key, &[db.clone(), db.clone(), db], &logger);
        assert_eq!(first, FirstStats::default());
        assert_eq!(source.engine.stats_calls, 1);
        // The failure is in the journal, naming the service and the container (#1507).
        let lines = lines.lock().unwrap();
        assert_eq!(lines.len(), 1, "{lines:#?}");
        assert!(
            lines[0].contains("priming stats of gitsrv/db container abbd59dcacc8 failed")
                && lines[0].contains("its other replicas are not asked either"),
            "{lines:#?}"
        );
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
            let mut before = demo_source(
                &collector,
                FixtureEngine::demo(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            before.clock = test_clock;
            two_rounds(&mut before, H13 + 10 * 60_000, 5_000);
            Source::stop(&mut before, &quiet());
            collector.stop().expect("stop");
        }
        // gitsrv/db was restarted (same id, new StartedAt) and its new cgroup has already written
        // more than the old counter: a baseline, not a too-small delta.
        let collector = test_collector();
        collector.start().expect("start");
        let mut engine = FixtureEngine::demo();
        add_written(&mut engine, 1, GITSRV_DB, 1_000_000);
        let inspect = engine
            .inspects
            .iter_mut()
            .find(|(id, _)| id.starts_with(GITSRV_DB))
            .unwrap()
            .1;
        inspect.state.started_at = "2026-09-29T13:15:00Z".into();
        let mut after = demo_source(&collector, engine, &DockerConfig::default(), Some(path));
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
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
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
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 30 * 60_000, 5_000);
        // Next seen at 15:10: the 13:00 hour ended while the probe was not running.
        set_clock(H13 + 130 * 60_000);
        assert!(source.roll_written_hours(&quiet()).is_empty());
        collector.stop().expect("stop");
        let record = source.tracker.state.services[&key_of("gitsrv", "db")]
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
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        two_rounds(&mut source, H13 + 10 * 60_000, 5_000);
        // gitsrv/db is recreated: the same service under a new id whose counter starts afresh.
        let old = source
            .engine
            .list
            .iter()
            .position(|c| c.id.starts_with(GITSRV_DB))
            .unwrap();
        let new_id = format!("ffff{}", &source.engine.list[old].id[4..]);
        source.engine.list[old].id = new_id.clone();
        let inspect = source
            .engine
            .inspects
            .iter()
            .find(|(id, _)| id.starts_with(GITSRV_DB))
            .unwrap()
            .1
            .clone();
        source.engine.inspects.insert(new_id.clone(), inspect);
        for (round, entries) in source.engine.rounds.iter_mut().enumerate().take(2) {
            let mut stats = entries
                .iter()
                .find(|(id, _)| id.starts_with(GITSRV_DB))
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
            entries.insert(new_id.clone(), stats);
        }
        two_rounds(&mut source, H13 + 20 * 60_000, 5_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let posted = source.roll_written_hours(&quiet());
        collector.stop().expect("stop");
        let db = posted
            .iter()
            .find(|(key, _)| *key == key_of("gitsrv", "db"))
            .unwrap()
            .1;
        // Old container: 163_840. New one: its first sample is a baseline, then 500 bytes.
        assert_eq!(db.bytes, 163_840 + 500);
        let record = &source.tracker.state.services[&key_of("gitsrv", "db")];
        let baselines = &record.written.as_ref().unwrap().baselines;
        assert!(
            baselines.keys().all(|id| !id.starts_with(GITSRV_DB)),
            "old id pruned"
        );
    }

    // ---- The memory sensors of #1818 -------------------------------------------------------

    const HSM_APP: &str = "c5abbadb3674";
    const JANITOR: &str = "e9d7ff6f1b1e";

    /// A logger that keeps every line, Debug included.
    fn capturing() -> (Logger, std::sync::Arc<std::sync::Mutex<Vec<String>>>) {
        let lines = std::sync::Arc::new(std::sync::Mutex::new(Vec::new()));
        let sink = std::sync::Arc::clone(&lines);
        let logger = Logger::with_sink(Level::Debug, move |line: &str| {
            sink.lock().unwrap().push(line.to_string())
        });
        (logger, lines)
    }

    /// The round's debug line of one service.
    fn round_line(lines: &[String], node: &str) -> String {
        lines
            .iter()
            .rev()
            .find(|line| line.contains(&format!("docker: {node}: CPU")))
            .unwrap_or_else(|| panic!("no round line for {node}: {lines:#?}"))
            .clone()
    }

    /// Poll and two stats rounds over the cgroup captures, one sample period apart on the
    /// monotonic clock and `apart_ms` on the wall clock from `at_ms`.
    fn two_cgroup_rounds(
        source: &mut DockerSource<'_, FixtureEngine>,
        cgroups: &crate::probe_only::host::tests::FakeTree,
        logger: &Logger,
        at_ms: i64,
        apart_ms: i64,
    ) {
        let never = || false;
        set_clock(at_ms);
        source.engine.round = 0;
        cgroup::tests::demo_cgroups(cgroups, "cgroup", 0);
        source.poll(logger, &never).expect("poll");
        source.sample_stats(logger, &never).expect("round 0");
        set_clock(at_ms + apart_ms);
        source.engine.round = 1;
        cgroup::tests::demo_cgroups(cgroups, "cgroup", 1);
        advance_monotonic(source, contract::DEFAULT_SAMPLE_PERIOD);
        source.sample_stats(logger, &never).expect("round 1");
    }

    fn cgroup_source(collector: &Collector) -> DockerSource<'_, FixtureEngine> {
        let mut source = demo_source(
            collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            None,
        );
        source.clock = test_clock;
        source
    }

    #[test]
    fn the_memory_figures_of_a_round_are_what_the_captures_say() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        let cgroups = demo_cgroup_mount(&mut source);
        let (logger, lines) = capturing();
        set_clock(H13 + 10 * 60_000);
        source.engine.round = 0;
        source.poll(&logger, &|| false).expect("poll");
        source.sample_stats(&logger, &|| false).expect("round 0");
        {
            let lines = lines.lock().unwrap();
            // The first sample of a pressure counter is a baseline: skipped, not 0.
            let app = round_line(&lines, "Docker/hsm/app");
            assert!(
                app.contains("own memory 179.36 MB, pressure skipped, swap 1.59 MB"),
                "{app}"
            );
        }
        set_clock(H13 + 11 * 60_000);
        source.engine.round = 1;
        cgroup::tests::demo_cgroups(&cgroups, "cgroup", 1);
        advance_monotonic(&mut source, contract::DEFAULT_SAMPLE_PERIOD);
        source.sample_stats(&logger, &|| false).expect("round 1");
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        // hsm/app: anon + shmem 188_076_032 B; 1.8 s of stall in the 60 s between the samples;
        // 1_671_168 B of swap.
        let app = round_line(&lines, "Docker/hsm/app");
        assert!(
            app.contains("own memory 179.36 MB, pressure 3.00 %, swap 1.59 MB"),
            "{app}"
        );
        // gitsrv/db: a real 0 of swap; 0.6 s of stall = 1 %.
        let db = round_line(&lines, "Docker/gitsrv/db");
        assert!(db.contains("pressure 1.00 %, swap 0.00 MB"), "{db}");
        // lumina/mdb has no memory.pressure (no PSI): no pressure value at all.
        let mdb = round_line(&lines, "Docker/lumina/mdb");
        assert!(mdb.contains("pressure skipped, swap 0.00 MB"), "{mdb}");
        // panelhost has no cgroup v2 directory: its own memory still comes from the stats.
        let panelhost = round_line(&lines, "Docker/panelhost/panelhost");
        assert!(
            panelhost.contains("own memory 23.90 MB, pressure skipped, swap skipped"),
            "{panelhost}"
        );
        // ... and that is said once, not every round.
        let missing = lines
            .iter()
            .filter(|line| line.contains("7717692f78a8: no cgroup v2 directory"))
            .count();
        assert_eq!(missing, 1, "{lines:#?}");
        drop(cgroups);
    }

    #[test]
    fn pressure_takes_the_worst_replica_and_a_skipped_replica_skips_the_sample() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        let cgroups = demo_cgroup_mount(&mut source);
        // gitsrv/db gets a second replica: janitor's container under db's service (5 % against
        // db's 1 %).
        let janitor = source
            .engine
            .list
            .iter()
            .position(|c| c.id.starts_with(JANITOR))
            .unwrap();
        let labels = source
            .engine
            .list
            .iter()
            .find(|c| c.id.starts_with(GITSRV_DB))
            .unwrap()
            .labels
            .clone();
        source.engine.list[janitor].labels = labels;
        let (logger, lines) = capturing();
        two_cgroup_rounds(&mut source, &cgroups, &logger, H13 + 10 * 60_000, 60_000);
        let line = round_line(&lines.lock().unwrap(), "Docker/gitsrv/db");
        assert!(line.contains("pressure 5.00 %"), "{line}");

        // Now one replica's counter goes backwards: that replica is skipped, so is the service.
        lines.lock().unwrap().clear();
        let dir = format!(
            "cgroup/system.slice/docker-{}.scope",
            source.engine.list[janitor].id
        );
        cgroups.file(
            &format!("{dir}/memory.pressure"),
            "some avg10=0.00 avg60=0.00 avg300=0.00 total=1\n",
        );
        advance_monotonic(&mut source, contract::DEFAULT_SAMPLE_PERIOD);
        source.sample_stats(&logger, &|| false).expect("round 2");
        collector.stop().expect("stop");
        let line = round_line(&lines.lock().unwrap(), "Docker/gitsrv/db");
        assert!(line.contains("pressure skipped"), "{line}");
    }

    #[test]
    fn an_oom_kill_inside_a_running_container_latches_oom_killed() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        let cgroups = demo_cgroup_mount(&mut source);
        let (logger, lines) = capturing();
        let janitor = key_of("lumina-ci", "janitor");
        set_clock(H13);
        source.poll(&logger, &|| false).expect("poll");
        source.sample_stats(&logger, &|| false).expect("round 0");
        // The first sample only sets the baseline.
        assert_eq!(
            source.tracker.state.services[&janitor].oom_latch_until,
            None
        );
        // janitor's oom_kill goes 0 → 1 while State.OOMKilled stays false (it keeps running).
        source.engine.round = 1;
        cgroup::tests::demo_cgroups(&cgroups, "cgroup", 1);
        source.sample_stats(&logger, &|| false).expect("round 1");
        collector.stop().expect("stop");
        let until = source.tracker.state.services[&janitor]
            .oom_latch_until
            .expect("latched");
        assert!(until >= unix_now() + 23 * 3600, "{until}");
        assert!(lines.lock().unwrap().iter().any(|line| line.contains(
            "docker: Docker/lumina-ci/janitor: a process of a running container was \
                       killed"
        )));
        // Nobody else was killed.
        assert_eq!(
            source.tracker.state.services[&key_of("hsm", "app")].oom_latch_until,
            None
        );
        // The next poll reports the latch like a State.OOMKilled one.
        let outcome = source.tracker.observe(
            &BTreeMap::from([(
                janitor.clone(),
                vec![ContainerObservation {
                    id: "x".into(),
                    state: "running".into(),
                    inspect: Some(InspectFacts::default()),
                }],
            )]),
            unix_now(),
            source.oom_latch,
        );
        assert_eq!(outcome.reports[&janitor].oom_killed, Some(true));
    }

    #[test]
    fn limit_hits_are_posted_once_per_hour_and_summed_over_the_hour() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        let cgroups = demo_cgroup_mount(&mut source);
        two_cgroup_rounds(&mut source, &cgroups, &quiet(), H13 + 30 * 60_000, 60_000);
        set_clock(H13 + 60 * 60_000 + 1_000);
        let (written, hits) = source.roll_hourly(&quiet());
        collector.stop().expect("stop");
        assert_eq!(written.len(), 11);
        // Every service with memory.events and a limit measured the hour (10: not panelhost).
        assert_eq!(hits.len(), 10, "{hits:?}");
        let count = |service: &(&str, &str)| {
            hits.iter()
                .find(|(key, _)| *key == key_of(service.0, service.1))
                .map(|(_, hour)| hour.count())
        };
        // hsm/app's max went 957 → 960, janitor's 12 → 14; an idle limited service is a real 0.
        assert_eq!(count(&("hsm", "app")), Some(3));
        assert_eq!(count(&("lumina-ci", "janitor")), Some(2));
        assert_eq!(count(&("gitsrv", "db")), Some(0));
        assert_eq!(count(&("panelhost", "panelhost")), None);
        let app = hits
            .iter()
            .find(|(key, _)| *key == key_of("hsm", "app"))
            .unwrap()
            .1;
        assert_eq!(app.comment(), "13:00–14:00 UTC; measured 1 of 60 min");
        // Posted once.
        set_clock(H13 + 60 * 60_000 + 61_000);
        assert!(source.roll_hourly(&quiet()).1.is_empty());
    }

    #[test]
    fn a_restarted_container_only_sets_a_limit_hit_baseline() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        let cgroups = demo_cgroup_mount(&mut source);
        let never = || false;
        set_clock(H13 + 10 * 60_000);
        source.poll(&quiet(), &never).expect("poll");
        source.sample_stats(&quiet(), &never).expect("round 0");
        // hsm/app restarts (same id, new StartedAt) before the next round; its new cgroup's max
        // (960) is not 3 hits on top of the old one's 957.
        let inspect = source
            .engine
            .inspects
            .iter_mut()
            .find(|(id, _)| id.starts_with(HSM_APP))
            .unwrap()
            .1;
        inspect.state.started_at = "2026-09-29T13:10:30Z".into();
        set_clock(H13 + 11 * 60_000);
        source.poll(&quiet(), &never).expect("poll");
        source.engine.round = 1;
        cgroup::tests::demo_cgroups(&cgroups, "cgroup", 1);
        source.sample_stats(&quiet(), &never).expect("round 1");
        set_clock(H13 + 60 * 60_000 + 1_000);
        let (_, hits) = source.roll_hourly(&quiet());
        collector.stop().expect("stop");
        assert!(
            hits.iter().all(|(key, _)| *key != key_of("hsm", "app")),
            "no delta measured: nothing posted, {hits:?}"
        );
        let janitor = hits
            .iter()
            .find(|(key, _)| *key == key_of("lumina-ci", "janitor"))
            .unwrap();
        assert_eq!(janitor.1.count(), 2);
    }

    #[test]
    fn limit_hits_continue_the_hour_across_a_probe_restart() {
        let dir = std::env::temp_dir().join(format!(
            "hsm-probe-docker-limit-hits-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut before = demo_source(
                &collector,
                FixtureEngine::demo(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            before.clock = test_clock;
            let cgroups = demo_cgroup_mount(&mut before);
            two_cgroup_rounds(&mut before, &cgroups, &quiet(), H13 + 30 * 60_000, 60_000);
            Source::stop(&mut before, &quiet());
            collector.stop().expect("stop");
        }
        assert!(std::fs::read_to_string(&path)
            .unwrap()
            .contains("\"limitHits\""));
        // Back at 13:40; hsm/app hit its limit 4 more times while the probe was down.
        let collector = test_collector();
        collector.start().expect("start");
        let mut after = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            Some(path),
        );
        after.clock = test_clock;
        let cgroups = demo_cgroup_mount(&mut after);
        cgroup::tests::demo_cgroups(&cgroups, "cgroup", 1);
        let app_dir = format!(
            "cgroup/system.slice/docker-{}.scope",
            after
                .engine
                .list
                .iter()
                .find(|c| c.id.starts_with(HSM_APP))
                .unwrap()
                .id
        );
        cgroups.file(
            &format!("{app_dir}/memory.events"),
            "low 0\nhigh 0\nmax 964\noom 0\noom_kill 0\noom_group_kill 0\n",
        );
        set_clock(H13 + 40 * 60_000);
        after.engine.round = 1;
        after.poll(&quiet(), &|| false).expect("poll");
        after.sample_stats(&quiet(), &|| false).expect("round");
        set_clock(H13 + 60 * 60_000 + 1_000);
        let (_, hits) = after.roll_hourly(&quiet());
        collector.stop().expect("stop");
        let app = hits
            .iter()
            .find(|(key, _)| *key == key_of("hsm", "app"))
            .unwrap();
        assert_eq!(app.1.count(), 3 + 4, "before the stop plus the downtime");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_service_without_a_memory_limit_gets_no_limit_hits() {
        let collector = test_collector();
        collector.start().expect("start");
        let mut source = cgroup_source(&collector);
        // hsm/app runs without mem_limit: Docker reports the host's memory as its limit.
        for round in &mut source.engine.rounds {
            for (id, stats) in round.iter_mut() {
                if id.starts_with(HSM_APP) {
                    stats.memory_stats.limit = Some(DEMO_MEM_TOTAL + 4096);
                }
            }
        }
        let cgroups = demo_cgroup_mount(&mut source);
        two_cgroup_rounds(&mut source, &cgroups, &quiet(), H13 + 10 * 60_000, 60_000);
        let paths = registered(&collector);
        collector.stop().expect("stop");
        let has = |p: &str| paths.iter().any(|x| x == p);
        assert!(!has(".probe/Docker/hsm/app/Memory limit hits per hour"));
        assert!(has(".probe/Docker/hsm/app/Memory pressure %"));
        assert!(has(
            ".probe/Docker/lumina-ci/janitor/Memory limit hits per hour"
        ));
        assert!(source.tracker.state.services[&key_of("hsm", "app")]
            .limit_hits
            .is_none());
    }

    #[test]
    fn swap_used_registers_once_swap_was_seen_and_is_remembered() {
        let dir =
            std::env::temp_dir().join(format!("hsm-probe-docker-swap-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(state::STATE_FILE_NAME);
        {
            let collector = test_collector();
            collector.start().expect("start");
            let mut source = demo_source(
                &collector,
                FixtureEngine::demo(),
                &DockerConfig::default(),
                Some(path.clone()),
            );
            source.clock = test_clock;
            let cgroups = demo_cgroup_mount(&mut source);
            // gitsrv/db swaps once in the first round, nothing in the second.
            let db_dir = format!(
                "cgroup/system.slice/docker-{}.scope",
                source
                    .engine
                    .list
                    .iter()
                    .find(|c| c.id.starts_with(GITSRV_DB))
                    .unwrap()
                    .id
            );
            set_clock(H13);
            source.poll(&quiet(), &|| false).expect("poll");
            cgroups.file(&format!("{db_dir}/memory.swap.current"), "4096\n");
            source.sample_stats(&quiet(), &|| false).expect("round 0");
            assert!(source.tracker.state.services[&key_of("gitsrv", "db")].swap_seen);
            // The next poll writes it to the state file.
            source.poll(&quiet(), &|| false).expect("poll");
            Source::stop(&mut source, &quiet());
            collector.stop().expect("stop");
        }
        // After an agent restart gitsrv/db swaps nothing, yet its Swap used registers (and reads
        // 0) from the priming round on; a service that never swapped still has none.
        let collector = test_collector();
        let mut source = demo_source(
            &collector,
            FixtureEngine::demo(),
            &DockerConfig::default(),
            Some(path),
        );
        let _cgroups = demo_cgroup_mount(&mut source);
        assert!(source.swap_seen(&key_of("gitsrv", "db")), "remembered");
        source.prime(&quiet()).expect("prime");
        collector.start().expect("start");
        let paths = registered(&collector);
        collector.stop().expect("stop");
        assert!(
            paths
                .iter()
                .any(|p| p == ".probe/Docker/gitsrv/db/Swap used"),
            "{paths:#?}"
        );
        assert!(!paths
            .iter()
            .any(|p| p == ".probe/Docker/lumina/mdb/Swap used"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_cgroup_registration_rules() {
        let memory = |pressure: Option<u64>, max: Option<u64>, swap: Option<u64>| CgroupMemory {
            events: Some(cgroup::MemoryEvents {
                max,
                oom_kill: Some(0),
            }),
            pressure_some_us: pressure,
            swap_bytes: swap,
        };
        let all = memory(Some(1), Some(0), Some(1));
        assert_eq!(
            cgroup_sensors(&all, true, false),
            CgroupSensors {
                pressure: true,
                limit_hits: true,
                swap: true
            }
        );
        // No limit: no limit hits, whatever memory.events says.
        assert!(!cgroup_sensors(&all, false, false).limit_hits);
        // memory.events without a `max` line, or not readable at all.
        assert!(!cgroup_sensors(&memory(Some(1), None, Some(0)), true, false).limit_hits);
        let no_events = CgroupMemory {
            events: None,
            ..all.clone()
        };
        assert!(!cgroup_sensors(&no_events, true, false).limit_hits);
        // Swap: 0 bytes registers nothing until swap was seen; then 0 registers too.
        let no_swap = memory(Some(1), Some(0), Some(0));
        assert!(!cgroup_sensors(&no_swap, true, false).swap);
        assert!(cgroup_sensors(&no_swap, true, true).swap);
        // No memory.swap.current (no swap accounting): never, remembered or not.
        assert!(!cgroup_sensors(&memory(Some(1), Some(0), None), true, true).swap);
        // No PSI: no pressure.
        assert!(!cgroup_sensors(&memory(None, Some(0), Some(0)), true, false).pressure);
    }
}
