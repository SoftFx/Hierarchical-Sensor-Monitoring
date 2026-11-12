//! Network (#1711): the Windows hosts' `.computer/Network/…` sensors, fed from `/proc/net`.
//!
//! # Placement
//!
//! Probe-only, by the owner's standing rule for the Linux epic (#1413): new Linux sources live in
//! the probe (the precedent is [`super::top_cpu`]). The collector reads these counters only on
//! Windows (PDH, `GetIfTable2`); the managed collector has no Unix network sensors. Root rule #10
//! (one acquisition mechanism per sensor path) is about the two collectors on the *same* OS; across
//! OSes what carries over is the meaning, and both read the same RFC 1213 counters (table below).
//! A managed Unix implementation, if one is ever written, must read these same `/proc` files.
//!
//! # Wire contract — the Windows one, so alert templates and dashboards carry over
//!
//! | path (`.computer/Network/…`) | Windows | here |
//! |---|---|---|
//! | `Connections Established Count` | PDH `Connections Established`, TCPv4 + TCPv6 | `/proc/net/snmp` `Tcp: CurrEstab` |
//! | `Connection Failures Count` | PDH `Connection Failures`, delta | `Tcp: AttemptFails`, delta ([`procfs::counter_delta`]) |
//! | `Connections Reset Count` | PDH `Connections Reset`, delta | `Tcp: EstabResets`, delta |
//! | `<iface>/Received MB,sec`, `<iface>/Sent MB,sec` | `GetIfTable2` octets (`RunNetworkSpeedLoop`) | `/proc/net/dev` octets ([`procfs::speeds`]) |
//!
//! The three TCP sensors are the collector's own catalog rows
//! ([`Collector::network_counter_sensor`]), so their registration is the Windows one byte for byte
//! (Int, KeepHistory 90 days, Grafana on, the catalog descriptions); the probe only feeds them, once
//! a minute (the catalog's post period). A delta is posted only when non-zero, as on Windows: a
//! quiet minute posts nothing, which is why the two delta sensors have TTL "never", while
//! `Connections Established Count` (TTL 5 min), read from the same file every minute, goes to
//! Timeout when the source stops.
//!
//! The speed bars copy `RunNetworkSpeedLoop`'s registration: DoubleBar, 1-minute bars of samples
//! taken every [`SAMPLE_PERIOD`] (the agent's 10 s), precision 2, `MBytes_sec`, EMA,
//! KeepHistory 90 days, TTL 5 min, Grafana on, the same description; created when a direction
//! first moves at least [`MIN_DISPLAYABLE`] MB/s, never for an idle one. Two differences, both
//! from what a custom bar can express through the C ABI: the probe's bars are posted when they
//! close (Windows also posts the open bar every 15 s; the stored minute is the same), and they
//! register `Alerts: null` where Windows sends an empty list (both: no alert).
//!
//! # Which interfaces
//!
//! Windows reports every Up, non-loopback interface except NDIS filter modules. Here, by default,
//! only physical ones ([`procfs::is_physical`]): a Docker host's `veth` pairs get a new name each
//! time a container is recreated and would leave a trail of timed-out sensors. `probe.network.
//! interfaces` (name patterns) replaces that rule; loopback is never reported. At most
//! [`MAX_INTERFACES`] interfaces get sensors, per source.
//!
//! # Linux-only additions (no Windows counterpart)
//!
//! - [`health`]: per interface, `Link up`, `Carrier changes`, `Errors`, `Drops`.
//! - [`wifi`]: per Wi-Fi interface, a `Signal dBm` bar.
//! - [`reachability`] (opt-in): TCP connect time and failures to the gateway and one internet
//!   address.

pub mod health;
pub mod procfs;
pub mod reachability;
pub mod wifi;

use std::collections::{BTreeMap, BTreeSet};
use std::path::PathBuf;
use std::time::{Duration, Instant};

use hsm_collector::{
    Collector, DoubleBarSensor, IntSensor, NetworkCounter, SensorOptions, STATISTICS_EMA,
};

use self::procfs::{DevCounters, TcpCounters};
use super::disks::mounts::glob_matches;
use super::{FailureLog, HostEnvironment, Source};
use crate::config::NetworkConfig;
use crate::logging::Logger;

/// `RevealDefaultPath("Network", …, is_computer_sensor = true)`.
pub const CATEGORY: &str = ".computer/Network";
pub const RECEIVED_LEAF: &str = "Received MB,sec";
pub const SENT_LEAF: &str = "Sent MB,sec";

/// The catalog rows' post period (`post_period_ms = 60000`).
pub const TCP_PERIOD: Duration = Duration::from_secs(60);
/// HsmAgent's `hsm_collector_enable_network_interface_speed_sensors(collector, 10000)`.
pub const SAMPLE_PERIOD: Duration = Duration::from_secs(10);
/// `CreateDefaultBarSensor(path, DOUBLE_BAR, 60000, kDefaultBarPrecision, …)`.
pub const BAR_PERIOD: Duration = Duration::from_secs(60);
/// Windows `MarkBuiltInPushBar(15000, 15000)`; carried by the ABI, not partially posted here.
const BAR_POST_PERIOD: Duration = Duration::from_secs(15);
/// `kDefaultBarPrecision`.
const BAR_PRECISION: i32 = 2;
pub const SPEED_TTL: Duration = Duration::from_secs(5 * 60);
/// `kKeepHistory90dMs`.
pub const KEEP_HISTORY: Duration = Duration::from_secs(90 * 24 * 3600);
/// `Unit.MBytes_sec`.
pub const UNIT_MBYTES_SEC: i32 = 2103;
/// The smallest MB/s that does not round to 0.00 at precision 2 (Windows: `0.5 / 10^2`): slower
/// trickle (keepalives) is treated as no traffic, so it never creates a sensor of zeros.
pub const MIN_DISPLAYABLE: f64 = 0.005;
/// A bound on interfaces with sensors (the server has no sensor delete; a pattern like `*` on a
/// Docker host would otherwise grow the tree with every recreated container).
pub const MAX_INTERFACES: usize = 32;

/// `RunNetworkSpeedLoop`: `<Average received|Average sent> network speed on interface **<if>**.`
pub fn speed_description(direction: Direction, interface: &str) -> String {
    let average = match direction {
        Direction::Received => "Average received",
        Direction::Sent => "Average sent",
    };
    format!("{average} network speed on interface **{interface}**.")
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Direction {
    Received,
    Sent,
}

/// Register the network sources. The TCP sensors register now (before Start); the speed sensors
/// register at runtime, when an interface first carries traffic.
pub fn register<'c>(
    collector: &'c Collector,
    config: &NetworkConfig,
    environment: &HostEnvironment,
    logger: &Logger,
) -> Vec<Box<dyn Source + 'c>> {
    if !config.enabled {
        logger.info("network: disabled (probe.network.enabled = false)");
        return Vec::new();
    }
    let mut sources: Vec<Box<dyn Source + 'c>> = Vec::new();
    match TcpConnections::register(collector, environment.proc_net.join("snmp")) {
        Ok(source) => sources.push(Box::new(source)),
        Err(error) => logger.error(format!(
            "network: cannot register the TCP connection sensors: {error}"
        )),
    }
    let filter = InterfaceFilter::new(config, environment);
    sources.push(Box::new(InterfaceSpeed::new(
        collector,
        filter.clone(),
        environment.proc_net.join("dev"),
    )));
    sources.push(Box::new(health::InterfaceHealth::new(
        collector,
        filter.clone(),
        environment.proc_net.join("dev"),
    )));
    sources.push(Box::new(wifi::WifiSignal::new(
        collector,
        filter,
        environment.proc_net.join("wireless"),
    )));
    if config.reachability.enabled {
        match reachability::Reachability::register(
            collector,
            &config.reachability,
            environment.proc_net.join("route"),
        ) {
            Ok(source) => sources.push(Box::new(source)),
            Err(error) => logger.error(format!(
                "network: cannot register the reachability sensors: {error}"
            )),
        }
    }
    sources
}

/// Which interfaces the per-interface sources report: never loopback (as on Windows,
/// `IF_TYPE_SOFTWARE_LOOPBACK`); with no `probe.network.interfaces` patterns, the physical ones.
#[derive(Clone, Debug)]
pub struct InterfaceFilter {
    sys_root: PathBuf,
    patterns: Vec<String>,
}

impl InterfaceFilter {
    pub fn new(config: &NetworkConfig, environment: &HostEnvironment) -> Self {
        Self {
            sys_root: environment.sys_root.clone(),
            patterns: config.interfaces.clone(),
        }
    }

    pub fn sys_root(&self) -> &std::path::Path {
        &self.sys_root
    }

    pub fn reported(&self, name: &str) -> bool {
        if procfs::is_loopback(&self.sys_root, name) {
            return false;
        }
        if self.patterns.is_empty() {
            procfs::is_physical(&self.sys_root, name)
        } else {
            self.patterns
                .iter()
                .any(|pattern| glob_matches(pattern, name))
        }
    }
}

/// The bound of [`MAX_INTERFACES`] interfaces with sensors, one per source. An interface once
/// admitted keeps its slot (the server has no sensor delete, so freeing it would not shrink the
/// tree); the first refusal is logged.
pub struct InterfaceCap {
    what: &'static str,
    admitted: BTreeSet<String>,
    logged: bool,
}

impl InterfaceCap {
    pub fn new(what: &'static str) -> Self {
        Self {
            what,
            admitted: BTreeSet::new(),
            logged: false,
        }
    }

    pub fn admit(&mut self, interface: &str, logger: &Logger) -> bool {
        if self.admitted.contains(interface) {
            return true;
        }
        if self.admitted.len() >= MAX_INTERFACES {
            if !self.logged {
                logger.warn(format!(
                    "network: {MAX_INTERFACES} interfaces have {} sensors, the cap; '{interface}' \
                     and later ones get none (narrow probe.network.interfaces)",
                    self.what
                ));
                self.logged = true;
            }
            return false;
        }
        self.admitted.insert(interface.to_string());
        true
    }
}

/// The path of a per-interface sensor.
pub fn interface_path(interface: &str, leaf: &str) -> String {
    format!("{CATEGORY}/{interface}/{leaf}")
}

/// The three TCP counters, one read of `/proc/net/snmp` a minute.
struct TcpConnections<'c> {
    snmp: PathBuf,
    established: IntSensor<'c>,
    failures: IntSensor<'c>,
    resets: IntSensor<'c>,
    /// The previous reading of the cumulative counters; `None` until the first good read.
    previous: Option<TcpCounters>,
    read_failures: FailureLog,
    post_failures: FailureLog,
}

impl<'c> TcpConnections<'c> {
    fn register(collector: &'c Collector, snmp: PathBuf) -> hsm_collector::Result<Self> {
        Ok(Self {
            snmp,
            established: collector
                .network_counter_sensor(NetworkCounter::ConnectionsEstablished)?,
            failures: collector.network_counter_sensor(NetworkCounter::ConnectionFailures)?,
            resets: collector.network_counter_sensor(NetworkCounter::ConnectionsReset)?,
            previous: None,
            read_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        })
    }

    /// The values one reading posts: the gauge always, each delta only when [`counter_delta`]
    /// gives one. The reading becomes the next baseline.
    ///
    /// [`counter_delta`]: procfs::counter_delta
    fn values(&mut self, now: TcpCounters) -> (i32, Option<i32>, Option<i32>) {
        let previous = self.previous.replace(now);
        (
            i32::try_from(now.curr_estab).unwrap_or(i32::MAX),
            procfs::counter_delta(previous.map(|p| p.attempt_fails), now.attempt_fails),
            procfs::counter_delta(previous.map(|p| p.estab_resets), now.estab_resets),
        )
    }
}

const TCP_WHAT: &str = "network: TCP connections (/proc/net/snmp)";

impl Source for TcpConnections<'_> {
    fn name(&self) -> &'static str {
        "network tcp"
    }

    fn period(&self) -> Duration {
        TCP_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        let counters = std::fs::read_to_string(&self.snmp)
            .map_err(|error| format!("cannot read {}: {error}", self.snmp.display()))
            .and_then(|text| procfs::parse_tcp(&text));
        let counters = match counters {
            Ok(counters) => counters,
            Err(reason) => {
                // The baseline is kept (Windows `PdhReadDelta` does the same): the next good read
                // posts what accumulated across the failure.
                self.read_failures.failed(logger, TCP_WHAT, &reason);
                return;
            }
        };
        self.read_failures.succeeded(logger, TCP_WHAT);

        let (established, failures, resets) = self.values(counters);
        let mut result = self.established.add(established);
        if let Some(delta) = failures {
            result = result.and(self.failures.add(delta));
        }
        if let Some(delta) = resets {
            result = result.and(self.resets.add(delta));
        }
        match result {
            Ok(()) => self.post_failures.succeeded(logger, "network: TCP post"),
            Err(error) => {
                self.post_failures
                    .failed(logger, "network: TCP post", &error.to_string())
            }
        }
    }
}

/// Per-interface receive/send speed from `/proc/net/dev`, sampled every [`SAMPLE_PERIOD`].
struct InterfaceSpeed<'c> {
    collector: &'c Collector,
    dev: PathBuf,
    filter: InterfaceFilter,
    /// The previous reading of the reported interfaces and when it was taken.
    previous: Option<(Instant, BTreeMap<String, DevCounters>)>,
    received: BTreeMap<String, DoubleBarSensor<'c>>,
    sent: BTreeMap<String, DoubleBarSensor<'c>>,
    /// Interfaces with speed sensors.
    cap: InterfaceCap,
    read_failures: FailureLog,
    registration_failures: FailureLog,
    post_failures: FailureLog,
}

const SPEED_WHAT: &str = "network: interface speed (/proc/net/dev)";

impl<'c> InterfaceSpeed<'c> {
    fn new(collector: &'c Collector, filter: InterfaceFilter, dev: PathBuf) -> Self {
        Self {
            collector,
            dev,
            filter,
            previous: None,
            received: BTreeMap::new(),
            sent: BTreeMap::new(),
            cap: InterfaceCap::new("speed"),
            read_failures: FailureLog::default(),
            registration_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        }
    }

    /// One sample taken at `now`: speeds over the interval since the previous one, posted.
    fn sample_at(&mut self, dev: Result<String, String>, now: Instant, logger: &Logger) {
        let current = match dev.and_then(|text| procfs::parse_dev(&text)) {
            Ok(all) => all
                .into_iter()
                .filter(|(name, _)| self.filter.reported(name))
                .collect::<BTreeMap<_, _>>(),
            Err(reason) => {
                // Windows drops the baseline on a failed read: a speed over the whole gap divided
                // by the short interval after it would spike for one sample.
                self.previous = None;
                self.read_failures.failed(logger, SPEED_WHAT, &reason);
                return;
            }
        };
        self.read_failures.succeeded(logger, SPEED_WHAT);

        if let Some((then, previous)) = self.previous.take() {
            let speeds = procfs::speeds(&previous, &current, now.saturating_duration_since(then));
            let mut registrations = Ok(());
            let mut posts = Ok(());
            for (name, speed) in &speeds {
                for (direction, value) in [
                    (Direction::Received, speed.rx_mb_per_sec),
                    (Direction::Sent, speed.tx_mb_per_sec),
                ] {
                    self.post(
                        name,
                        direction,
                        value,
                        logger,
                        &mut registrations,
                        &mut posts,
                    );
                }
            }
            report(
                &mut self.registration_failures,
                logger,
                "network: speed registration",
                registrations,
            );
            report(
                &mut self.post_failures,
                logger,
                "network: speed post",
                posts,
            );
        }
        self.previous = Some((now, current));
    }

    fn post(
        &mut self,
        interface: &str,
        direction: Direction,
        value: f64,
        logger: &Logger,
        registrations: &mut Result<(), String>,
        posts: &mut Result<(), String>,
    ) {
        if value < MIN_DISPLAYABLE || !self.cap.admit(interface, logger) {
            return;
        }
        let (sensors, leaf) = match direction {
            Direction::Received => (&mut self.received, RECEIVED_LEAF),
            Direction::Sent => (&mut self.sent, SENT_LEAF),
        };
        if !sensors.contains_key(interface) {
            let path = interface_path(interface, leaf);
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_unit(UNIT_MBYTES_SEC)
                .with_statistics(STATISTICS_EMA)
                .with_keep_history(KEEP_HISTORY)
                .with_ttl(SPEED_TTL)
                .with_enable_grafana(true)
                .with_description(speed_description(direction, interface));
            match self.collector.double_bar_sensor(
                &path,
                BAR_PERIOD,
                BAR_POST_PERIOD,
                BAR_PRECISION,
                &options,
            ) {
                Ok(sensor) => {
                    sensors.insert(interface.to_string(), sensor);
                }
                Err(error) => {
                    // Not cached: the next sample tries again.
                    *registrations = Err(format!("{path}: {error}"));
                    return;
                }
            }
        }
        if let Some(sensor) = sensors.get(interface) {
            if let Err(error) = sensor.add(value) {
                *posts = Err(format!("{}: {error}", interface_path(interface, leaf)));
            }
        }
    }
}

/// Log a sample's registration or post outcome once per episode.
pub(crate) fn report(
    log: &mut FailureLog,
    logger: &Logger,
    what: &str,
    outcome: Result<(), String>,
) {
    match outcome {
        Ok(()) => log.succeeded(logger, what),
        Err(reason) => log.failed(logger, what, &reason),
    }
}

impl Source for InterfaceSpeed<'_> {
    fn name(&self) -> &'static str {
        "network speed"
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
pub(crate) mod tests {
    use super::procfs::tests::{wifi_docker_sysfs, WIFI_DOCKER_DEV, WSL_SNMP};
    use super::*;
    use crate::logging::Level;
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;
    use std::sync::{Arc, Mutex};

    /// The TCP sensors every host registers (the Windows catalog rows).
    pub const TCP_SET: &[&str] = &[
        ".computer/Network/Connection Failures Count",
        ".computer/Network/Connections Established Count",
        ".computer/Network/Connections Reset Count",
    ];

    /// A fake `/proc/net` + `/sys/class/net` of a Wi-Fi host running Docker.
    pub fn fake_host(tree: &FakeTree) {
        tree.file("proc-net/snmp", WSL_SNMP);
        tree.file("proc-net/dev", WIFI_DOCKER_DEV);
        wifi_docker_sysfs(tree);
    }

    pub fn capture() -> (Logger, Arc<Mutex<Vec<String>>>) {
        let lines = Arc::new(Mutex::new(Vec::new()));
        let logger = Logger::with_sink(Level::Debug, {
            let lines = Arc::clone(&lines);
            move |line: &str| lines.lock().unwrap().push(line.to_string())
        });
        (logger, lines)
    }

    pub fn network_registrations(collector: &Collector) -> Vec<String> {
        collector
            .registrations()
            .into_iter()
            .filter(|json| json.contains("\"Path\":\".computer/Network/"))
            .collect()
    }

    /// The filter of a fake host.
    pub fn test_filter(config: &NetworkConfig, tree: &FakeTree) -> InterfaceFilter {
        InterfaceFilter {
            sys_root: tree.0.join("sys"),
            patterns: config.interfaces.clone(),
        }
    }

    fn speed_source<'c>(
        collector: &'c Collector,
        tree: &FakeTree,
        patterns: &[&str],
    ) -> InterfaceSpeed<'c> {
        let config = NetworkConfig {
            enabled: true,
            interfaces: patterns.iter().map(|p| p.to_string()).collect(),
            ..NetworkConfig::default()
        };
        InterfaceSpeed::new(
            collector,
            test_filter(&config, tree),
            tree.0.join("proc-net/dev"),
        )
    }

    /// `/proc/net/dev` text with the given octets per interface.
    fn dev(rows: &[(&str, u64, u64)]) -> String {
        let mut text = String::from(
            "Inter-|   Receive                                                |  Transmit\n \
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs \
             drop fifo colls carrier compressed\n",
        );
        for (name, rx, tx) in rows {
            text.push_str(&format!(
                "{name:>6}:{rx:>8} 0 0 0 0 0 0 0 {tx:>8} 0 0 0 0 0 0 0\n"
            ));
        }
        text
    }

    #[test]
    fn the_tcp_sensors_are_the_windows_catalog_rows() {
        let collector = test_collector();
        let tree = FakeTree::new("net-tcp-wire");
        fake_host(&tree);
        let environment = HostEnvironment {
            proc_net: tree.0.join("proc-net"),
            sys_root: tree.0.join("sys"),
            ..HostEnvironment::system()
        };
        let (logger, _) = capture();
        let sources = register(&collector, &NetworkConfig::default(), &environment, &logger);
        assert_eq!(sources.len(), 4, "tcp, speed, health, Wi-Fi");
        collector.start().expect("start");
        let registrations = network_registrations(&collector);
        collector.stop().expect("stop");

        let mut paths: Vec<String> = registrations
            .iter()
            .map(|json| {
                let start = json.find("\"Path\":\"").unwrap() + 8;
                json[start..start + json[start..].find('"').unwrap()].to_string()
            })
            .collect();
        paths.sort();
        assert_eq!(paths, TCP_SET);
        for json in &registrations {
            for part in [
                "\"SensorType\":1,",
                "\"KeepHistory\":77760000000000",
                "\"IsSingletonSensor\":true",
                "\"EnableGrafana\":true",
            ] {
                assert!(json.contains(part), "missing {part} in {json}");
            }
        }
        let find = |leaf: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("/{leaf}\"")))
                .unwrap()
                .clone()
        };
        // TTL never (Int64.MaxValue ticks, the server's "None" interval) for the two deltas,
        // which post nothing in a quiet minute; 5 minutes for the count, posted every minute.
        let never = "\"TTLTicks\":[9223372036854775807]";
        let established = find("Connections Established Count");
        assert!(
            established.contains("\"TTLTicks\":[3000000000]"),
            "{established}"
        );
        assert!(established.contains("Established TCP connections."));
        for (leaf, description) in [
            ("Connection Failures Count", "TCP connection failures."),
            ("Connections Reset Count", "TCP connections reset."),
        ] {
            let json = find(leaf);
            assert!(json.contains(never) && json.contains(description), "{json}");
        }
    }

    #[test]
    fn tcp_deltas_post_only_what_moved() {
        let collector = test_collector();
        let mut source =
            TcpConnections::register(&collector, PathBuf::from("/nonexistent")).unwrap();
        let counters = |estab: u64, fails: u64, resets: u64| TcpCounters {
            curr_estab: estab,
            attempt_fails: fails,
            estab_resets: resets,
        };
        assert_eq!(
            source.values(counters(12, 100, 7)),
            (12, None, None),
            "baseline"
        );
        assert_eq!(
            source.values(counters(13, 100, 7)),
            (13, None, None),
            "quiet minute"
        );
        assert_eq!(source.values(counters(9, 104, 9)), (9, Some(4), Some(2)));
        assert_eq!(
            source.values(counters(9, 3, 9)),
            (9, None, None),
            "counter went down"
        );
        assert_eq!(
            source.values(counters(9, 5, 9)),
            (9, Some(2), None),
            "from the new baseline"
        );
    }

    #[test]
    fn an_unreadable_snmp_file_is_logged_once_and_keeps_the_baseline() {
        let collector = test_collector();
        let tree = FakeTree::new("net-tcp-broken");
        let mut source = TcpConnections::register(&collector, tree.0.join("snmp")).unwrap();
        let (logger, lines) = capture();
        collector.start().expect("start");
        tree.file(
            "snmp",
            "Tcp: CurrEstab AttemptFails EstabResets\nTcp: 3 10 1\n",
        );
        source.sample(&logger);
        std::fs::remove_file(tree.0.join("snmp")).unwrap();
        source.sample(&logger);
        source.sample(&logger);
        tree.file(
            "snmp",
            "Tcp: CurrEstab AttemptFails EstabResets\nTcp: 3 15 1\n",
        );
        source.sample(&logger);
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        assert_eq!(
            lines.iter().filter(|l| l.contains("cannot read")).count(),
            1,
            "{lines:#?}"
        );
        assert_eq!(
            lines.iter().filter(|l| l.contains("recovered")).count(),
            1,
            "{lines:#?}"
        );
        assert_eq!(source.previous.map(|p| p.attempt_fails), Some(15));
    }

    #[test]
    fn busy_physical_interfaces_get_the_windows_bar_shape_at_runtime() {
        let collector = test_collector();
        let tree = FakeTree::new("net-speed-wire");
        wifi_docker_sysfs(&tree);
        let mut source = speed_source(&collector, &tree, &[]);
        let (logger, _) = capture();
        collector.start().expect("start");
        let t0 = Instant::now();
        let mb = 1024 * 1024;
        let all = |wlan_rx: u64, wlan_tx: u64| {
            dev(&[
                ("lo", 50 * mb, 50 * mb),
                ("wlp3s0", wlan_rx, wlan_tx),
                ("enp2s0", 0, 0),
                ("docker0", 9 * mb, 9 * mb),
                ("veth1a2b3c4", 3 * mb, 3 * mb),
            ])
        };
        source.sample_at(Ok(all(0, 0)), t0, &logger);
        assert!(
            network_registrations(&collector).is_empty(),
            "the baseline registers nothing"
        );

        // 10 s later: wlp3s0 received 20 MB and sent 10 KB (below 0.005 MB/s: no Sent sensor);
        // the virtual interfaces and loopback moved too, but are not physical.
        let later = |lo: u64| {
            dev(&[
                ("lo", lo * mb, lo * mb),
                ("wlp3s0", 20 * mb, 10 * 1024),
                ("enp2s0", 0, 0),
                ("docker0", 90 * mb, 90 * mb),
                ("veth1a2b3c4", 30 * mb, 30 * mb),
            ])
        };
        source.sample_at(Ok(later(500)), t0 + Duration::from_secs(10), &logger);
        let registrations = network_registrations(&collector);
        collector.stop().expect("stop");
        assert_eq!(registrations.len(), 1, "{registrations:#?}");
        let json = &registrations[0];
        for part in [
            "\"Path\":\".computer/Network/wlp3s0/Received MB,sec\"",
            "\"SensorType\":5,",
            "\"OriginalUnit\":2103,",
            "\"Statistics\":1",
            "\"TTLTicks\":[3000000000]",
            "\"KeepHistory\":77760000000000",
            "\"EnableGrafana\":true",
            "\"IsSingletonSensor\":true",
            "Average received network speed on interface **wlp3s0**.",
        ] {
            assert!(json.contains(part), "missing {part} in {json}");
        }
        assert!(source.received.contains_key("wlp3s0") && source.sent.is_empty());
    }

    #[test]
    fn patterns_replace_the_physical_rule_but_never_report_loopback() {
        let collector = test_collector();
        let tree = FakeTree::new("net-speed-patterns");
        wifi_docker_sysfs(&tree);
        let source = speed_source(&collector, &tree, &["br-*", "lo", "wlp*"]);
        let reported: Vec<&str> = [
            "lo",
            "wlp3s0",
            "enp2s0",
            "docker0",
            "br-5f1c2a9e0b7d",
            "veth1a2b3c4",
        ]
        .into_iter()
        .filter(|name| source.filter.reported(name))
        .collect();
        assert_eq!(reported, ["wlp3s0", "br-5f1c2a9e0b7d"]);
    }

    #[test]
    fn a_failed_read_drops_the_baseline_and_is_logged_once() {
        let collector = test_collector();
        let tree = FakeTree::new("net-speed-broken");
        wifi_docker_sysfs(&tree);
        let mut source = speed_source(&collector, &tree, &[]);
        let (logger, lines) = capture();
        collector.start().expect("start");
        let t0 = Instant::now();
        let mb = 1024 * 1024;
        source.sample_at(Ok(dev(&[("wlp3s0", 0, 0)])), t0, &logger);
        source.sample_at(
            Err("cannot read dev: EIO".into()),
            t0 + Duration::from_secs(10),
            &logger,
        );
        source.sample_at(
            Err("cannot read dev: EIO".into()),
            t0 + Duration::from_secs(20),
            &logger,
        );
        // The first good read after the gap is a new baseline: no speed over the 30 s gap divided
        // by a 10 s interval.
        source.sample_at(
            Ok(dev(&[("wlp3s0", 300 * mb, 0)])),
            t0 + Duration::from_secs(30),
            &logger,
        );
        assert!(network_registrations(&collector).is_empty());
        source.sample_at(
            Ok(dev(&[("wlp3s0", 310 * mb, 0)])),
            t0 + Duration::from_secs(40),
            &logger,
        );
        assert_eq!(network_registrations(&collector).len(), 1);
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        assert_eq!(
            lines.iter().filter(|l| l.contains("EIO")).count(),
            1,
            "{lines:#?}"
        );
        assert_eq!(
            lines.iter().filter(|l| l.contains("recovered")).count(),
            1,
            "{lines:#?}"
        );
    }

    #[test]
    fn interfaces_stop_at_the_cap() {
        let collector = test_collector();
        let tree = FakeTree::new("net-speed-cap");
        let mut source = speed_source(&collector, &tree, &["*"]);
        let (logger, lines) = capture();
        collector.start().expect("start");
        let names: Vec<String> = (0..MAX_INTERFACES + 3)
            .map(|i| format!("veth{i}"))
            .collect();
        let rows = |bytes: u64| -> Vec<(&str, u64, u64)> {
            names
                .iter()
                .map(|name| (name.as_str(), bytes, bytes))
                .collect()
        };
        let t0 = Instant::now();
        source.sample_at(Ok(dev(&rows(0))), t0, &logger);
        source.sample_at(
            Ok(dev(&rows(1024 * 1024))),
            t0 + Duration::from_secs(10),
            &logger,
        );
        source.sample_at(
            Ok(dev(&rows(2 * 1024 * 1024))),
            t0 + Duration::from_secs(20),
            &logger,
        );
        collector.stop().expect("stop");
        assert_eq!(network_registrations(&collector).len(), 2 * MAX_INTERFACES);
        let lines = lines.lock().unwrap();
        assert_eq!(
            lines.iter().filter(|l| l.contains("the cap")).count(),
            1,
            "{lines:#?}"
        );
    }

    #[test]
    fn off_when_disabled() {
        let collector = test_collector();
        let (logger, lines) = capture();
        let config = NetworkConfig {
            enabled: false,
            interfaces: Vec::new(),
            ..NetworkConfig::default()
        };
        assert!(register(&collector, &config, &HostEnvironment::system(), &logger).is_empty());
        assert!(lines.lock().unwrap().iter().any(|l| l.contains("disabled")));
    }
}
