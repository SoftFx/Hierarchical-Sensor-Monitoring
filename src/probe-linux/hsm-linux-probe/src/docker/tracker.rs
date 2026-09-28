//! The per-service state machines of the 60-second state poll: service status, health, restart
//! count, OOM latch and vanished-service memory.
//!
//! Pure: a poll's observations plus the wall clock in, the values to post out. Nothing here talks
//! to Docker or to the collector, so every edge (recreate, replica churn, a failed inspect, latch
//! expiry, a service that disappears) is unit-tested directly.
//!
//! Aggregation over replicas: status and health are **worst-of** (one stopped replica makes the
//! service `Stopped`; one unhealthy replica makes it `unhealthy`), restart counts are summed. A
//! service whose inspect was incomplete this poll reports only what the listing alone proves (its
//! status); health, restarts and OOM wait for the next complete poll instead of being guessed.

use std::collections::BTreeMap;
use std::time::Duration;

use super::contract::{self, health, service_status};
use super::identity::ServiceKey;
use super::state::{ServiceRecord, State};

/// How far `last_seen` and the OOM latch may lag before the change is worth an SSD write. The
/// retention they drive is measured in days, so an hour of slack is invisible.
const PERSIST_GRANULARITY_SECS: i64 = 3600;

/// One listed container of a service.
#[derive(Clone, Debug)]
pub struct ContainerObservation {
    pub id: String,
    /// The listing's `State` (created, restarting, running, removing, paused, exited, dead).
    pub state: String,
    /// `None` when this container's inspect failed this poll.
    pub inspect: Option<InspectFacts>,
}

#[derive(Clone, Debug, Default)]
pub struct InspectFacts {
    pub restart_count: u64,
    pub oom_killed: bool,
    /// `State.Health.Status`; `None` when the container has no healthcheck.
    pub health: Option<String>,
}

/// What to post for one service after a poll. `None` fields are not posted this time.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct ServiceReport {
    /// Listed this poll (false = remembered, vanished).
    pub present: bool,
    /// A `Service status` key.
    pub status: Option<i32>,
    /// A container of the service defines a healthcheck (register `Health`).
    pub has_healthcheck: bool,
    /// A `Health` key.
    pub health: Option<i32>,
    /// A new cumulative restart count to post; `None` when unchanged since the last post.
    pub restart_count: Option<u64>,
    pub oom_killed: Option<bool>,
}

/// Docker container state → `Service status` key (the Windows `ServiceControllerStatus` values).
pub fn service_status_of(state: &str) -> Option<i32> {
    match state {
        "running" => Some(service_status::RUNNING),
        "created" | "restarting" => Some(service_status::START_PENDING),
        "paused" => Some(service_status::PAUSED),
        "exited" | "dead" | "removing" => Some(service_status::STOPPED),
        _ => None,
    }
}

/// Lower = worse. `Stopped` is the worst; a crash-looping `StartPending` is worse than `Paused`.
fn status_rank(key: i32) -> u8 {
    match key {
        service_status::STOPPED => 0,
        service_status::START_PENDING => 1,
        service_status::PAUSED => 2,
        _ => 3,
    }
}

/// `State.Health.Status` → `Health` key. `none` (healthcheck disabled) and unknown values carry no
/// health information and are not posted.
pub fn health_of(status: &str) -> Option<i32> {
    match status {
        "starting" => Some(health::STARTING),
        "healthy" => Some(health::HEALTHY),
        "unhealthy" => Some(health::UNHEALTHY),
        _ => None,
    }
}

fn health_rank(key: i32) -> u8 {
    match key {
        health::UNHEALTHY => 0,
        health::STARTING => 1,
        _ => 2,
    }
}

/// The result of one poll.
#[derive(Debug, Default)]
pub struct PollOutcome {
    pub reports: BTreeMap<ServiceKey, ServiceReport>,
    /// Vanished services past retention, dropped from the state this poll.
    pub forgotten: Vec<ServiceKey>,
}

/// Owns the persisted [`State`] and advances it poll by poll.
#[derive(Debug, Default)]
pub struct Tracker {
    pub state: State,
    dirty: bool,
}

impl Tracker {
    pub fn new(state: State) -> Self {
        Self {
            state,
            dirty: false,
        }
    }

    /// Whether the state changed in a way worth writing since the last call.
    pub fn take_dirty(&mut self) -> bool {
        std::mem::take(&mut self.dirty)
    }

    /// Record that `value` reached the `Restart count` sensor.
    pub fn restart_posted(&mut self, key: &ServiceKey, value: u64) {
        if let Some(record) = self.state.services.get_mut(key) {
            if record.restart_posted != Some(value) {
                record.restart_posted = Some(value);
                self.dirty = true;
            }
        }
    }

    /// Advance every service by one poll. `services` is the complete listing, grouped.
    pub fn observe(
        &mut self,
        services: &BTreeMap<ServiceKey, Vec<ContainerObservation>>,
        now: i64,
        oom_latch: Duration,
    ) -> PollOutcome {
        let mut outcome = PollOutcome::default();

        for (key, containers) in services {
            let record = self.state.services.entry(key.clone()).or_insert_with(|| {
                self.dirty = true;
                ServiceRecord::new(key)
            });
            let report = observe_service(record, containers, now, oom_latch, &mut self.dirty);
            outcome.reports.insert(key.clone(), report);
        }

        // Services remembered but not listed: removed containers. Stopped for the retention
        // window after the last sighting, then forgotten.
        let retention =
            i64::try_from(contract::VANISHED_SERVICE_RETENTION.as_secs()).unwrap_or(i64::MAX);
        let vanished: Vec<ServiceKey> = self
            .state
            .services
            .keys()
            .filter(|key| !services.contains_key(*key))
            .cloned()
            .collect();
        for key in vanished {
            let last_seen = self.state.services[&key].last_seen;
            if now.saturating_sub(last_seen) > retention {
                self.state.services.remove(&key);
                self.dirty = true;
                outcome.forgotten.push(key);
            } else {
                outcome.reports.insert(
                    key,
                    ServiceReport {
                        present: false,
                        status: Some(service_status::STOPPED),
                        ..ServiceReport::default()
                    },
                );
            }
        }

        outcome
    }
}

fn observe_service(
    record: &mut ServiceRecord,
    containers: &[ContainerObservation],
    now: i64,
    oom_latch: Duration,
    dirty: &mut bool,
) -> ServiceReport {
    if record.last_seen == 0 || now - record.last_seen >= PERSIST_GRANULARITY_SECS {
        record.last_seen = now;
        *dirty = true;
    }

    let status = containers
        .iter()
        .filter_map(|c| service_status_of(&c.state))
        .min_by_key(|key| status_rank(*key));
    let has_healthcheck = containers
        .iter()
        .any(|c| c.inspect.as_ref().is_some_and(|i| i.health.is_some()));

    let complete = containers.iter().all(|c| c.inspect.is_some());
    if !complete {
        return ServiceReport {
            present: true,
            status,
            has_healthcheck,
            ..ServiceReport::default()
        };
    }
    let facts: Vec<(&str, &InspectFacts)> = containers
        .iter()
        .filter_map(|c| c.inspect.as_ref().map(|i| (c.id.as_str(), i)))
        .collect();

    let health = facts
        .iter()
        .filter_map(|(_, i)| i.health.as_deref().and_then(health_of))
        .min_by_key(|key| health_rank(*key));

    // Restart count: per-container deltas folded into a service total that never goes down. A
    // container id the record has not seen (first sight, or a recreate) contributes its whole
    // RestartCount — a fresh container starts at 0, so a recreate adds nothing.
    for (id, inspect) in &facts {
        let delta = match record.containers.get(*id) {
            None => inspect.restart_count,
            Some(previous) if inspect.restart_count >= *previous => {
                inspect.restart_count - previous
            }
            // The same id's counter went down (not something Docker does): a new baseline.
            Some(_) => inspect.restart_count,
        };
        if delta > 0 {
            record.restart_total = record.restart_total.saturating_add(delta);
            *dirty = true;
        }
        if record.containers.get(*id) != Some(&inspect.restart_count) {
            record
                .containers
                .insert((*id).to_string(), inspect.restart_count);
            *dirty = true;
        }
    }
    let before = record.containers.len();
    record
        .containers
        .retain(|id, _| facts.iter().any(|(listed, _)| listed == id));
    if record.containers.len() != before {
        *dirty = true;
    }
    let restart_count =
        (record.restart_posted != Some(record.restart_total)).then_some(record.restart_total);

    // OOM: latched for `oom_latch` after the last poll that saw OOMKilled, surviving recreate
    // (the latch is per service, on disk).
    let latch = i64::try_from(oom_latch.as_secs()).unwrap_or(i64::MAX);
    if facts.iter().any(|(_, i)| i.oom_killed) {
        let until = now.saturating_add(latch);
        let stale = record
            .oom_latch_until
            .is_none_or(|current| until - current >= PERSIST_GRANULARITY_SECS);
        if stale {
            record.oom_latch_until = Some(until);
            *dirty = true;
        }
    }
    if record.oom_latch_until.is_some_and(|until| now >= until) {
        record.oom_latch_until = None;
        *dirty = true;
    }
    let oom_killed = Some(record.oom_latch_until.is_some());

    ServiceReport {
        present: true,
        status,
        has_healthcheck,
        health,
        restart_count,
        oom_killed,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const DAY: i64 = 86_400;
    const LATCH: Duration = Duration::from_secs(24 * 3600);

    fn key(service: &str) -> ServiceKey {
        ServiceKey::new("proj", service)
    }

    fn container(id: &str, state: &str, restarts: u64) -> ContainerObservation {
        ContainerObservation {
            id: id.into(),
            state: state.into(),
            inspect: Some(InspectFacts {
                restart_count: restarts,
                ..InspectFacts::default()
            }),
        }
    }

    fn poll(
        tracker: &mut Tracker,
        now: i64,
        services: Vec<(ServiceKey, Vec<ContainerObservation>)>,
    ) -> PollOutcome {
        tracker.observe(&services.into_iter().collect(), now, LATCH)
    }

    /// Post what the tracker asked for, as the source does after a successful post.
    fn post_restarts(tracker: &mut Tracker, outcome: &PollOutcome) {
        for (key, report) in &outcome.reports {
            if let Some(value) = report.restart_count {
                tracker.restart_posted(key, value);
            }
        }
    }

    #[test]
    fn docker_states_map_onto_the_windows_service_statuses() {
        use service_status::*;
        assert_eq!(service_status_of("running"), Some(RUNNING));
        assert_eq!(service_status_of("created"), Some(START_PENDING));
        assert_eq!(service_status_of("restarting"), Some(START_PENDING));
        assert_eq!(service_status_of("paused"), Some(PAUSED));
        for stopped in ["exited", "dead", "removing"] {
            assert_eq!(service_status_of(stopped), Some(STOPPED), "{stopped}");
        }
        assert_eq!(service_status_of("weird"), None);
    }

    #[test]
    fn health_maps_and_none_carries_no_information() {
        assert_eq!(health_of("starting"), Some(health::STARTING));
        assert_eq!(health_of("healthy"), Some(health::HEALTHY));
        assert_eq!(health_of("unhealthy"), Some(health::UNHEALTHY));
        assert_eq!(health_of("none"), None);
    }

    #[test]
    fn replicas_report_the_worst_status_and_health() {
        let mut tracker = Tracker::default();
        let mut sick = container("b", "running", 0);
        sick.inspect.as_mut().unwrap().health = Some("unhealthy".into());
        let mut fine = container("a", "running", 0);
        fine.inspect.as_mut().unwrap().health = Some("healthy".into());
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("web"), vec![fine, sick, container("c", "exited", 0)])],
        );
        let report = &outcome.reports[&key("web")];
        assert_eq!(report.status, Some(service_status::STOPPED));
        assert_eq!(report.health, Some(health::UNHEALTHY));
        assert!(report.has_healthcheck);

        let outcome = poll(
            &mut tracker,
            1_060,
            vec![(
                key("web"),
                vec![
                    container("a", "running", 0),
                    container("b", "restarting", 0),
                    container("c", "paused", 0),
                ],
            )],
        );
        assert_eq!(
            outcome.reports[&key("web")].status,
            Some(service_status::START_PENDING)
        );
    }

    #[test]
    fn a_service_without_a_healthcheck_reports_no_health() {
        let mut tracker = Tracker::default();
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("app"), vec![container("a", "running", 0)])],
        );
        let report = &outcome.reports[&key("app")];
        assert!(!report.has_healthcheck);
        assert_eq!(report.health, None);
    }

    #[test]
    fn restart_count_is_posted_once_then_only_on_change() {
        let mut tracker = Tracker::default();
        let first = poll(
            &mut tracker,
            1_000,
            vec![(key("db"), vec![container("a", "running", 2)])],
        );
        assert_eq!(first.reports[&key("db")].restart_count, Some(2));
        post_restarts(&mut tracker, &first);

        let same = poll(
            &mut tracker,
            1_060,
            vec![(key("db"), vec![container("a", "running", 2)])],
        );
        assert_eq!(same.reports[&key("db")].restart_count, None);

        let grew = poll(
            &mut tracker,
            1_120,
            vec![(key("db"), vec![container("a", "running", 3)])],
        );
        assert_eq!(grew.reports[&key("db")].restart_count, Some(3));
    }

    #[test]
    fn a_recreate_starts_a_new_baseline_and_the_count_never_goes_down() {
        let mut tracker = Tracker::default();
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("db"), vec![container("old", "running", 4)])],
        );
        post_restarts(&mut tracker, &outcome);
        // `docker compose up` recreated it: a new id whose own RestartCount starts at 0.
        let outcome = poll(
            &mut tracker,
            1_060,
            vec![(key("db"), vec![container("new", "running", 0)])],
        );
        assert_eq!(outcome.reports[&key("db")].restart_count, None, "still 4");
        assert_eq!(tracker.state.services[&key("db")].restart_total, 4);
        assert!(!tracker.state.services[&key("db")]
            .containers
            .contains_key("old"));
        // The new container crash-loops twice: 4 + 2.
        let outcome = poll(
            &mut tracker,
            1_120,
            vec![(key("db"), vec![container("new", "running", 2)])],
        );
        assert_eq!(outcome.reports[&key("db")].restart_count, Some(6));
    }

    #[test]
    fn the_baseline_survives_a_probe_restart_through_the_state() {
        let mut tracker = Tracker::default();
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("db"), vec![container("a", "running", 7)])],
        );
        post_restarts(&mut tracker, &outcome);
        // Restart the probe: a new tracker over the persisted state (via JSON, as on disk).
        let text = tracker.state.to_json();
        let mut restarted = Tracker::new(State::parse(&text).unwrap());
        let outcome = poll(
            &mut restarted,
            5_000,
            vec![(key("db"), vec![container("a", "running", 7)])],
        );
        assert_eq!(
            outcome.reports[&key("db")].restart_count,
            None,
            "no re-post after a probe restart"
        );
    }

    #[test]
    fn replica_restarts_are_summed() {
        let mut tracker = Tracker::default();
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(
                key("worker"),
                vec![container("a", "running", 1), container("b", "running", 2)],
            )],
        );
        assert_eq!(outcome.reports[&key("worker")].restart_count, Some(3));
    }

    #[test]
    fn an_incomplete_inspect_reports_only_the_status() {
        let mut tracker = Tracker::default();
        let mut failed = container("b", "running", 9);
        failed.inspect = None;
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("web"), vec![container("a", "running", 1), failed])],
        );
        let report = &outcome.reports[&key("web")];
        assert_eq!(report.status, Some(service_status::RUNNING));
        assert_eq!(report.restart_count, None);
        assert_eq!(report.oom_killed, None);
        assert_eq!(report.health, None);
        assert_eq!(tracker.state.services[&key("web")].restart_total, 0);
    }

    #[test]
    fn oom_is_latched_for_a_day_and_survives_recreate() {
        let mut tracker = Tracker::default();
        let t0 = 1_000_000;
        let mut killed = container("a", "exited", 0);
        killed.inspect.as_mut().unwrap().oom_killed = true;
        let outcome = poll(&mut tracker, t0, vec![(key("db"), vec![killed])]);
        assert_eq!(outcome.reports[&key("db")].oom_killed, Some(true));

        // Recreated a minute later; the new container is fine, the latch holds.
        let outcome = poll(
            &mut tracker,
            t0 + 60,
            vec![(key("db"), vec![container("b", "running", 0)])],
        );
        assert_eq!(outcome.reports[&key("db")].oom_killed, Some(true));

        // Just before expiry: still true; at expiry: false, and the latch is cleared.
        let outcome = poll(
            &mut tracker,
            t0 + DAY - 1,
            vec![(key("db"), vec![container("b", "running", 0)])],
        );
        assert_eq!(outcome.reports[&key("db")].oom_killed, Some(true));
        let outcome = poll(
            &mut tracker,
            t0 + DAY,
            vec![(key("db"), vec![container("b", "running", 0)])],
        );
        assert_eq!(outcome.reports[&key("db")].oom_killed, Some(false));
        assert_eq!(tracker.state.services[&key("db")].oom_latch_until, None);
    }

    #[test]
    fn a_container_that_stays_oom_killed_keeps_the_latch_without_rewriting_every_minute() {
        let mut tracker = Tracker::default();
        let mut killed = container("a", "exited", 0);
        killed.inspect.as_mut().unwrap().oom_killed = true;
        poll(&mut tracker, 1_000, vec![(key("db"), vec![killed.clone()])]);
        assert!(tracker.take_dirty());
        poll(&mut tracker, 1_060, vec![(key("db"), vec![killed.clone()])]);
        assert!(
            !tracker.take_dirty(),
            "a minute later is not worth an SSD write"
        );
        // Still OOM-killed a day later: the latch was extended, not allowed to lapse.
        let outcome = poll(&mut tracker, 1_000 + DAY, vec![(key("db"), vec![killed])]);
        assert_eq!(outcome.reports[&key("db")].oom_killed, Some(true));
    }

    #[test]
    fn a_vanished_service_is_stopped_for_seven_days_then_forgotten() {
        let mut tracker = Tracker::default();
        let t0 = 10_000_000;
        poll(
            &mut tracker,
            t0,
            vec![
                (key("db"), vec![container("a", "running", 0)]),
                (key("web"), vec![container("b", "running", 0)]),
            ],
        );
        // `docker compose rm db`: only web is listed now.
        let outcome = poll(
            &mut tracker,
            t0 + 60,
            vec![(key("web"), vec![container("b", "running", 0)])],
        );
        let report = &outcome.reports[&key("db")];
        assert!(!report.present);
        assert_eq!(report.status, Some(service_status::STOPPED));
        assert_eq!(report.restart_count, None);

        let web = || vec![(key("web"), vec![container("b", "running", 0)])];
        let outcome = poll(&mut tracker, t0 + 7 * DAY, web());
        assert!(
            outcome.reports.contains_key(&key("db")),
            "day 7: still reported"
        );
        let outcome = poll(&mut tracker, t0 + 7 * DAY + 1, web());
        assert!(!outcome.reports.contains_key(&key("db")));
        assert_eq!(outcome.forgotten, vec![key("db")]);
        assert!(!tracker.state.services.contains_key(&key("db")));
    }

    #[test]
    fn a_vanished_service_comes_back_as_itself() {
        let mut tracker = Tracker::default();
        let outcome = poll(
            &mut tracker,
            1_000,
            vec![(key("db"), vec![container("a", "running", 3)])],
        );
        post_restarts(&mut tracker, &outcome);
        poll(&mut tracker, 1_060, vec![]);
        let outcome = poll(
            &mut tracker,
            2_000,
            vec![(key("db"), vec![container("z", "running", 0)])],
        );
        let report = &outcome.reports[&key("db")];
        assert!(report.present);
        assert_eq!(report.status, Some(service_status::RUNNING));
        assert_eq!(report.restart_count, None, "the total is still 3");
    }

    #[test]
    fn steady_polls_do_not_dirty_the_state() {
        let mut tracker = Tracker::default();
        let services = || vec![(key("db"), vec![container("a", "running", 1)])];
        let outcome = poll(&mut tracker, 1_000, services());
        post_restarts(&mut tracker, &outcome);
        assert!(tracker.take_dirty());
        for minute in 1..30 {
            poll(&mut tracker, 1_000 + minute * 60, services());
            assert!(!tracker.take_dirty(), "minute {minute}");
        }
        // An hour on, last_seen is refreshed once.
        poll(&mut tracker, 1_000 + 3_600, services());
        assert!(tracker.take_dirty());
    }
}
