//! Interface health (#1711 group 2), per reported interface ([`InterfaceFilter`]):
//!
//! | leaf (`.computer/Network/<iface>/…`) | type | source | posted |
//! |---|---|---|---|
//! | `Link up` | Bool | `/sys/class/net/<if>/operstate` ([`procfs::link_up`]) | on change, and every [`LINK_REPOST`] |
//! | `Carrier changes` | Int | `/sys/class/net/<if>/carrier_changes`, delta | per minute, only when non-zero |
//! | `Errors` | Int | `/proc/net/dev` rx + tx `errs`, delta | per minute, only when non-zero |
//! | `Drops` | Int | `/proc/net/dev` rx + tx `drop`, delta | per minute, only when non-zero |
//!
//! The link is checked every [`SAMPLE_PERIOD`]; a flap shorter than that is missed by `Link up`
//! but counted by `Carrier changes`, which is why both exist. `Link up` is created the first time
//! the interface is seen up, so a port that never had a cable (an unused Ethernet port of a Wi-Fi
//! host) gets no sensor that is false forever. It is a state sensor (`AggregateData`, as the Docker
//! container states), TTL [`LINK_TTL`] — three missed reposts.
//!
//! The three counters follow the TCP delta rule ([`procfs::counter_delta`]): the first reading is
//! the baseline, a counter that went down is re-baselined, a quiet minute posts nothing. Their
//! sensors are created on the first non-zero minute and have no TTL of their own (as the Docker
//! `Restart count`, which is also posted only when it moves).

use std::collections::BTreeMap;
use std::path::PathBuf;
use std::time::{Duration, Instant};

use hsm_collector::{BoolSensor, Collector, IntSensor, SensorOptions};

use super::procfs::{self, DevCounters};
use super::{interface_path, report, InterfaceCap, InterfaceFilter, KEEP_HISTORY, SAMPLE_PERIOD};
use crate::logging::Logger;
use crate::probe_only::{FailureLog, Source};

pub const LINK_LEAF: &str = "Link up";
pub const CARRIER_CHANGES_LEAF: &str = "Carrier changes";
pub const ERRORS_LEAF: &str = "Errors";
pub const DROPS_LEAF: &str = "Drops";

/// An unchanged `Link up` is posted again this often, so the server can tell a steady link from a
/// probe that stopped.
pub const LINK_REPOST: Duration = Duration::from_secs(5 * 60);
pub const LINK_TTL: Duration = Duration::from_secs(15 * 60);
/// The window of the three counter deltas.
pub const COUNTER_PERIOD: Duration = Duration::from_secs(60);
/// `Unit.Count`.
const UNIT_COUNT: i32 = 1100;

/// One of the three per-minute counters.
#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub enum Counter {
    CarrierChanges,
    Errors,
    Drops,
}

impl Counter {
    pub const ALL: [Counter; 3] = [Counter::CarrierChanges, Counter::Errors, Counter::Drops];

    pub fn leaf(self) -> &'static str {
        match self {
            Counter::CarrierChanges => CARRIER_CHANGES_LEAF,
            Counter::Errors => ERRORS_LEAF,
            Counter::Drops => DROPS_LEAF,
        }
    }

    pub fn description(self, interface: &str) -> String {
        match self {
            Counter::CarrierChanges => format!(
                "Times the link of interface **{interface}** went up or down in the minute \
                 (`carrier_changes`). **Normally there is no value at all**: it is posted only \
                 when non-zero. One drop counts 2 (down, then up), so a 2 is one short loss of \
                 the cable or the Wi-Fi association, even one too short for `Link up` to see."
            ),
            Counter::Errors => format!(
                "Receive + transmit errors on interface **{interface}** in the minute \
                 (`/proc/net/dev` errs): frames that arrived damaged or could not be sent. \
                 **Normally there is no value at all**: it is posted only when non-zero. A few \
                 a day are noise; errors every minute point to a bad cable or port, or radio \
                 interference on Wi-Fi."
            ),
            Counter::Drops => format!(
                "Received + transmitted packets dropped on interface **{interface}** in the \
                 minute (`/proc/net/dev` drop): packets the driver or kernel discarded, for \
                 example a full receive queue or traffic nobody here listens for. Posted only \
                 when non-zero. A small steady count is common and harmless on many hosts \
                 (Wi-Fi drivers count broadcast noise here); what matters is a jump above the \
                 usual level, especially together with `Errors` or `Carrier changes`."
            ),
        }
    }
}

pub fn link_description(interface: &str) -> String {
    format!(
        "Whether interface **{interface}** has a link (`operstate` is `up`). **True is normal.** \
         False means no link: the cable is unplugged, or Wi-Fi is not associated with the access \
         point. Checked every 10 s; posted on change and every 5 minutes. A drop shorter than \
         10 s can be missed here; `Carrier changes` counts it."
    )
}

/// One interface's cumulative counters.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct HealthCounters {
    pub carrier_changes: Option<u64>,
    pub errors: u64,
    pub drops: u64,
}

impl HealthCounters {
    fn from_dev(dev: &DevCounters, carrier_changes: Option<u64>) -> Self {
        Self {
            carrier_changes,
            errors: dev.errors,
            drops: dev.drops,
        }
    }

    fn get(&self, counter: Counter) -> Option<u64> {
        match counter {
            Counter::CarrierChanges => self.carrier_changes,
            Counter::Errors => Some(self.errors),
            Counter::Drops => Some(self.drops),
        }
    }
}

struct LinkSensor<'c> {
    sensor: BoolSensor<'c>,
    /// The last value posted, and when.
    last: Option<(bool, Instant)>,
}

/// Whether a link reading is posted: on change, or when the last post is [`LINK_REPOST`] old.
pub fn link_due(last: Option<(bool, Instant)>, up: bool, now: Instant) -> bool {
    match last {
        None => true,
        Some((value, at)) => value != up || now.saturating_duration_since(at) >= LINK_REPOST,
    }
}

pub struct InterfaceHealth<'c> {
    collector: &'c Collector,
    dev: PathBuf,
    filter: InterfaceFilter,
    links: BTreeMap<String, LinkSensor<'c>>,
    counters: BTreeMap<(String, Counter), IntSensor<'c>>,
    /// The counters at the start of the current window, and when it started.
    window: Option<(Instant, BTreeMap<String, HealthCounters>)>,
    cap: InterfaceCap,
    read_failures: FailureLog,
    link_failures: FailureLog,
    registration_failures: FailureLog,
    post_failures: FailureLog,
}

const HEALTH_WHAT: &str = "network: interface health (/proc/net/dev)";

impl<'c> InterfaceHealth<'c> {
    pub fn new(collector: &'c Collector, filter: InterfaceFilter, dev: PathBuf) -> Self {
        Self {
            collector,
            dev,
            filter,
            links: BTreeMap::new(),
            counters: BTreeMap::new(),
            window: None,
            cap: InterfaceCap::new("link health"),
            read_failures: FailureLog::default(),
            link_failures: FailureLog::default(),
            registration_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        }
    }

    /// The deltas a reading at `now` closes, if it ends the window; the reading becomes the next
    /// window's baseline then (and when there is none yet). Inside a window nothing changes, so
    /// what moved during it is posted once, at its end.
    pub fn window_deltas(
        &mut self,
        mut current: BTreeMap<String, HealthCounters>,
        now: Instant,
    ) -> Vec<(String, Counter, i32)> {
        let previous = match self.window.take() {
            None => {
                self.window = Some((now, current));
                return Vec::new();
            }
            Some((start, previous)) if now.saturating_duration_since(start) >= COUNTER_PERIOD => {
                previous
            }
            Some(open) => {
                self.window = Some(open);
                return Vec::new();
            }
        };
        let mut deltas = Vec::new();
        for (name, counters) in &current {
            for counter in Counter::ALL {
                let Some(value) = counters.get(counter) else {
                    continue;
                };
                let before = previous.get(name).and_then(|p| p.get(counter));
                if let Some(delta) = procfs::counter_delta(before, value) {
                    deltas.push((name.clone(), counter, delta));
                }
            }
        }
        // A `carrier_changes` that could not be read now keeps the old baseline, so the next
        // window posts what moved across both instead of starting over.
        for (name, counters) in current.iter_mut() {
            if counters.carrier_changes.is_none() {
                counters.carrier_changes = previous.get(name).and_then(|p| p.carrier_changes);
            }
        }
        self.window = Some((now, current));
        deltas
    }

    /// One sample: `dev` is the text of `/proc/net/dev` (or why it could not be read).
    pub fn sample_at(&mut self, dev: Result<String, String>, now: Instant, logger: &Logger) {
        let all = match dev.and_then(|text| procfs::parse_dev(&text)) {
            Ok(all) => all,
            Err(reason) => {
                // The window is kept: the next good read posts what accumulated across the gap.
                self.read_failures.failed(logger, HEALTH_WHAT, &reason);
                return;
            }
        };
        self.read_failures.succeeded(logger, HEALTH_WHAT);

        let mut registrations = Ok(());
        let mut posts = Ok(());
        let mut link_outcome = Ok(());
        let mut current = BTreeMap::new();
        let reported: Vec<_> = all
            .into_iter()
            .filter(|(name, _)| self.filter.reported(name))
            .collect();
        for (name, dev) in &reported {
            match procfs::link_up(self.filter.sys_root(), name) {
                Ok(up) => self.post_link(name, up, now, logger, &mut registrations, &mut posts),
                Err(reason) => link_outcome = Err(reason),
            }
            let carrier_changes = procfs::carrier_changes(self.filter.sys_root(), name);
            current.insert(name.clone(), HealthCounters::from_dev(dev, carrier_changes));
        }
        // An interface with a `Link up` sensor that left /proc/net/dev (a USB adapter unplugged, a
        // driver reset that removed the netdev) has no link: posted once, not reposted, so a
        // deleted bridge or VPN device goes to Timeout instead of a permanent "down".
        let gone: Vec<String> = self
            .links
            .iter()
            .filter(|(name, link)| {
                !current.contains_key(*name) && link.last.map(|(up, _)| up) != Some(false)
            })
            .map(|(name, _)| name.clone())
            .collect();
        for name in gone {
            self.post_link(&name, false, now, logger, &mut registrations, &mut posts);
        }
        for (name, counter, delta) in self.window_deltas(current, now) {
            self.post_counter(
                &name,
                counter,
                delta,
                logger,
                &mut registrations,
                &mut posts,
            );
        }
        report(
            &mut self.link_failures,
            logger,
            "network: link state (/sys/class/net)",
            link_outcome,
        );
        report(
            &mut self.registration_failures,
            logger,
            "network: health registration",
            registrations,
        );
        report(
            &mut self.post_failures,
            logger,
            "network: health post",
            posts,
        );
    }

    fn post_link(
        &mut self,
        interface: &str,
        up: bool,
        now: Instant,
        logger: &Logger,
        registrations: &mut Result<(), String>,
        posts: &mut Result<(), String>,
    ) {
        if !self.links.contains_key(interface) {
            if !up || !self.cap.admit(interface, logger) {
                return;
            }
            let path = interface_path(interface, LINK_LEAF);
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_aggregate_data(true)
                .with_ttl(LINK_TTL)
                .with_keep_history(KEEP_HISTORY)
                .with_enable_grafana(true)
                .with_description(link_description(interface));
            match self.collector.bool_sensor(&path, &options) {
                Ok(sensor) => {
                    self.links
                        .insert(interface.to_string(), LinkSensor { sensor, last: None });
                }
                Err(error) => {
                    *registrations = Err(format!("{path}: {error}"));
                    return;
                }
            }
        }
        let Some(link) = self.links.get_mut(interface) else {
            return;
        };
        if !link_due(link.last, up, now) {
            return;
        }
        match link.sensor.add(up) {
            Ok(()) => link.last = Some((up, now)),
            Err(error) => {
                *posts = Err(format!("{}: {error}", interface_path(interface, LINK_LEAF)))
            }
        }
    }

    fn post_counter(
        &mut self,
        interface: &str,
        counter: Counter,
        delta: i32,
        logger: &Logger,
        registrations: &mut Result<(), String>,
        posts: &mut Result<(), String>,
    ) {
        let key = (interface.to_string(), counter);
        let path = interface_path(interface, counter.leaf());
        if !self.counters.contains_key(&key) {
            if !self.cap.admit(interface, logger) {
                return;
            }
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_unit(UNIT_COUNT)
                .with_keep_history(KEEP_HISTORY)
                .with_enable_grafana(true)
                .with_description(counter.description(interface));
            match self.collector.int_sensor(&path, &options) {
                Ok(sensor) => {
                    self.counters.insert(key.clone(), sensor);
                }
                Err(error) => {
                    // The delta is lost with the failed registration; the next window retries.
                    *registrations = Err(format!("{path}: {error}"));
                    return;
                }
            }
        }
        if let Some(sensor) = self.counters.get(&key) {
            if let Err(error) = sensor.add(delta) {
                *posts = Err(format!("{path}: {error}"));
            }
        }
    }
}

impl Source for InterfaceHealth<'_> {
    fn name(&self) -> &'static str {
        "network health"
    }

    fn period(&self) -> Duration {
        SAMPLE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        let dev = std::fs::read_to_string(&self.dev)
            .map_err(|error| format!("cannot read {}: {error}", self.dev.display()));
        self.sample_at(dev, Instant::now(), logger);
    }
}

#[cfg(test)]
mod tests {
    use super::super::procfs::tests::wifi_docker_sysfs;
    use super::super::tests::{capture, network_registrations, test_filter};
    use super::*;
    use crate::config::NetworkConfig;
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;

    /// `/proc/net/dev` text with the given (rx errs, rx drop, tx errs, tx drop) per interface.
    fn dev(rows: &[(&str, u64, u64, u64, u64)]) -> String {
        let mut text = String::from(
            "Inter-|   Receive                                                |  Transmit\n \
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs \
             drop fifo colls carrier compressed\n",
        );
        for (name, rx_errs, rx_drop, tx_errs, tx_drop) in rows {
            text.push_str(&format!(
                "{name:>6}:       0 0 {rx_errs} {rx_drop} 0 0 0 0        0 0 {tx_errs} {tx_drop} 0 0 0 0\n"
            ));
        }
        text
    }

    fn health_source<'c>(collector: &'c Collector, tree: &FakeTree) -> InterfaceHealth<'c> {
        InterfaceHealth::new(
            collector,
            test_filter(&NetworkConfig::default(), tree),
            tree.0.join("proc-net/dev"),
        )
    }

    fn leaf_registrations(collector: &Collector) -> Vec<String> {
        let mut paths: Vec<String> = network_registrations(collector)
            .iter()
            .map(|json| {
                let start = json.find("\"Path\":\"").unwrap() + 8;
                json[start..start + json[start..].find('"').unwrap()].to_string()
            })
            .collect();
        paths.sort();
        paths
    }

    #[test]
    fn errors_and_drops_are_the_rx_plus_tx_columns() {
        let parsed = procfs::parse_dev(&dev(&[("wlp3s0", 1, 20, 3, 400)])).unwrap();
        assert_eq!(parsed["wlp3s0"].errors, 4);
        assert_eq!(parsed["wlp3s0"].drops, 420);
    }

    #[test]
    fn a_link_is_posted_on_change_and_every_five_minutes() {
        let t0 = Instant::now();
        let at = |seconds: u64| t0 + Duration::from_secs(seconds);
        assert!(link_due(None, true, t0), "the first reading");
        assert!(!link_due(Some((true, t0)), true, at(10)));
        assert!(link_due(Some((true, t0)), false, at(10)), "a change");
        assert!(!link_due(Some((true, t0)), true, at(299)));
        assert!(link_due(Some((true, t0)), true, at(300)), "the repost");
    }

    #[test]
    fn deltas_close_with_the_minute_window() {
        let collector = test_collector();
        let tree = FakeTree::new("net-health-window");
        let mut source = health_source(&collector, &tree);
        let t0 = Instant::now();
        let at = |seconds: u64| t0 + Duration::from_secs(seconds);
        let reading = |carrier: Option<u64>, errors: u64, drops: u64| {
            BTreeMap::from([(
                "wlp3s0".to_string(),
                HealthCounters {
                    carrier_changes: carrier,
                    errors,
                    drops,
                },
            )])
        };
        assert!(
            source
                .window_deltas(reading(Some(2), 0, 100), t0)
                .is_empty(),
            "baseline"
        );
        assert!(
            source
                .window_deltas(reading(Some(4), 1, 150), at(30))
                .is_empty(),
            "inside the window"
        );
        assert_eq!(
            source.window_deltas(reading(Some(4), 1, 170), at(60)),
            [
                ("wlp3s0".to_string(), Counter::CarrierChanges, 2),
                ("wlp3s0".to_string(), Counter::Errors, 1),
                ("wlp3s0".to_string(), Counter::Drops, 70),
            ],
            "the window's whole movement, once"
        );
        assert!(
            source
                .window_deltas(reading(Some(4), 1, 170), at(120))
                .is_empty(),
            "a quiet minute"
        );
        assert_eq!(
            source.window_deltas(reading(None, 0, 175), at(180)),
            [("wlp3s0".to_string(), Counter::Drops, 5)],
            "carrier_changes unreadable; errors went down (re-baselined)"
        );
        assert_eq!(
            source.window_deltas(reading(Some(6), 0, 175), at(240)),
            [("wlp3s0".to_string(), Counter::CarrierChanges, 2)],
            "the unreadable minute kept the carrier baseline"
        );
    }

    #[test]
    fn the_link_sensor_appears_when_the_link_is_first_up() {
        let collector = test_collector();
        let tree = FakeTree::new("net-health-link");
        wifi_docker_sysfs(&tree);
        let mut source = health_source(&collector, &tree);
        let (logger, lines) = capture();
        collector.start().expect("start");
        let rows = [
            ("lo", 0, 0, 0, 0),
            ("wlp3s0", 0, 10, 0, 0),
            ("enp2s0", 0, 0, 0, 0),
            ("docker0", 0, 0, 0, 0),
        ];
        let t0 = Instant::now();
        source.sample_at(Ok(dev(&rows)), t0, &logger);
        // wlp3s0 is up; enp2s0 (no cable) gets nothing; docker0 and lo are not reported.
        assert_eq!(
            leaf_registrations(&collector),
            [".computer/Network/wlp3s0/Link up"]
        );
        let json = &network_registrations(&collector)[0];
        for part in [
            "\"SensorType\":0,",
            "\"AggregateData\":true",
            "\"TTLTicks\":[9000000000]",
            "\"KeepHistory\":77760000000000",
            "\"EnableGrafana\":true",
            "\"IsSingletonSensor\":true",
            "Whether interface **wlp3s0** has a link",
        ] {
            assert!(json.contains(part), "missing {part} in {json}");
        }

        // A minute later: wlp3s0 dropped 7 packets and flapped once; enp2s0 got a cable.
        tree.file("sys/class/net/wlp3s0/carrier_changes", "4\n");
        tree.file("sys/class/net/enp2s0/operstate", "up\n");
        let rows = [
            ("lo", 0, 0, 0, 0),
            ("wlp3s0", 0, 15, 0, 2),
            ("enp2s0", 0, 0, 0, 0),
            ("docker0", 0, 0, 0, 0),
        ];
        source.sample_at(Ok(dev(&rows)), t0 + COUNTER_PERIOD, &logger);
        assert_eq!(
            leaf_registrations(&collector),
            [
                ".computer/Network/enp2s0/Link up",
                ".computer/Network/wlp3s0/Carrier changes",
                ".computer/Network/wlp3s0/Drops",
                ".computer/Network/wlp3s0/Link up",
            ]
        );
        let drops = network_registrations(&collector)
            .into_iter()
            .find(|json| json.contains("/Drops\""))
            .unwrap();
        for part in [
            "\"SensorType\":1,",
            "\"OriginalUnit\":1100,",
            "\"TTLTicks\":null",
            "Received + transmitted packets dropped on interface **wlp3s0**",
        ] {
            assert!(drops.contains(part), "missing {part} in {drops}");
        }
        assert_eq!(source.links["wlp3s0"].last.map(|(up, _)| up), Some(true));

        // The Wi-Fi adapter is unplugged: its row leaves /proc/net/dev, and its link is down.
        let rows = [("lo", 0, 0, 0, 0), ("enp2s0", 0, 0, 0, 0)];
        let unplugged = t0 + COUNTER_PERIOD + Duration::from_secs(10);
        source.sample_at(Ok(dev(&rows)), unplugged, &logger);
        assert_eq!(source.links["wlp3s0"].last, Some((false, unplugged)));
        // Still gone past the repost period: not posted again, so the sensor times out instead of
        // reading "down" forever (a deleted bridge or VPN device would otherwise stay red).
        source.sample_at(Ok(dev(&rows)), unplugged + LINK_REPOST, &logger);
        collector.stop().expect("stop");
        assert_eq!(source.links["wlp3s0"].last, Some((false, unplugged)));
        assert!(lines.lock().unwrap().iter().all(|l| !l.contains("ERROR")));
    }

    #[test]
    fn an_unreadable_link_state_is_logged_once() {
        let collector = test_collector();
        let tree = FakeTree::new("net-health-broken");
        wifi_docker_sysfs(&tree);
        tree.file("sys/class/net/wlp3s0/operstate", "sideways\n");
        let mut source = health_source(&collector, &tree);
        let (logger, lines) = capture();
        collector.start().expect("start");
        let t0 = Instant::now();
        for i in 0..3 {
            source.sample_at(
                Ok(dev(&[("wlp3s0", 0, 0, 0, 0)])),
                t0 + Duration::from_secs(10 * i),
                &logger,
            );
        }
        source.sample_at(Err("cannot read dev: EIO".into()), t0, &logger);
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        assert_eq!(
            lines.iter().filter(|l| l.contains("sideways")).count(),
            1,
            "{lines:#?}"
        );
        assert_eq!(
            lines.iter().filter(|l| l.contains("EIO")).count(),
            1,
            "{lines:#?}"
        );
        assert!(network_registrations(&collector).is_empty());
    }
}
