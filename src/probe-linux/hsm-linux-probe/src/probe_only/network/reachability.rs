//! Reachability (#1711 group 4, **off unless `probe.network.reachability.enabled`**): every
//! [`PROBE_PERIOD`] the probe times a TCP connect to the default gateway and to one internet
//! address, so a stall of a few seconds shows in the tree.
//!
//! | path (`.computer/Network/Reachability/<Gateway\|Internet>/…`) | type | posted |
//! |---|---|---|
//! | `Connect time ms` | DoubleBar, `Milliseconds` | 1-minute bar of the successful connects |
//! | `Connect failures` | Int, `Count` | every minute: the failed attempts of the minute (0 too) |
//!
//! - **The connect is the standard library's** (`TcpStream::connect_timeout`); the probe writes
//!   no protocol code. No ICMP: a ping socket needs a capability the unit does not have.
//! - **A refused connect is an answer:** the host sent a reset, so it is reachable and the time
//!   is a round trip. Most home routers do not listen on every port.
//! - **A failure** is a timeout ([`CONNECT_TIMEOUT`]) or any other error (unreachable network), and
//!   for the gateway also "no default route".
//! - The gateway is the lowest-metric IPv4 default route of `/proc/net/route`, read each time, so
//!   a changed network is followed. An unreadable route file is logged once and skips the gateway
//!   attempt (it is not a network failure).
//! - The time bars have no TTL: while every attempt fails they get no value, and `Connect failures`
//!   (TTL 5 min, posted every minute) says why.

use std::io::ErrorKind;
use std::net::{IpAddr, Ipv4Addr, SocketAddr, TcpStream};
use std::path::PathBuf;
use std::time::{Duration, Instant};

use hsm_collector::{Collector, DoubleBarSensor, IntSensor, SensorOptions};

use super::{BAR_PERIOD, BAR_POST_PERIOD, BAR_PRECISION, CATEGORY, KEEP_HISTORY, SPEED_TTL};
use crate::config::ReachabilityConfig;
use crate::logging::Logger;
use crate::probe_only::{FailureLog, Source};

pub const FOLDER: &str = "Reachability";
pub const CONNECT_TIME_LEAF: &str = "Connect time ms";
pub const FAILURES_LEAF: &str = "Connect failures";
pub const PROBE_PERIOD: Duration = Duration::from_secs(5);
/// Both targets are tried in one sample, one after the other, so two timeouts fit the period.
pub const CONNECT_TIMEOUT: Duration = Duration::from_secs(2);
/// The window `Connect failures` counts over.
pub const FAILURE_WINDOW: Duration = Duration::from_secs(60);
/// `Unit.Milliseconds`.
const UNIT_MILLISECONDS: i32 = 1010;
/// `Unit.Count`.
const UNIT_COUNT: i32 = 1100;

/// `RTF_UP | RTF_GATEWAY` (`<linux/route.h>`).
const RTF_UP_GATEWAY: u32 = 0x0001 | 0x0002;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Target {
    Gateway,
    Internet,
}

impl Target {
    pub fn name(self) -> &'static str {
        match self {
            Target::Gateway => "Gateway",
            Target::Internet => "Internet",
        }
    }
}

pub fn target_path(target: Target, leaf: &str) -> String {
    format!("{CATEGORY}/{FOLDER}/{}/{leaf}", target.name())
}

/// Parse `/proc/net/route` and return the gateway of the lowest-metric IPv4 default route that is
/// up (`None`: no default route). Columns are found by the header's names; the addresses are the
/// kernel's `%08X` of a network-order word, so its native-endian bytes are the address.
pub fn default_gateway(route: &str) -> Result<Option<Ipv4Addr>, String> {
    let mut lines = route.lines();
    let header: Vec<&str> = lines
        .next()
        .ok_or("/proc/net/route is empty")?
        .split_whitespace()
        .collect();
    let column = |name: &str| {
        header
            .iter()
            .position(|column| *column == name)
            .ok_or_else(|| format!("no {name} column in /proc/net/route"))
    };
    let (destination, gateway, flags, metric, mask) = (
        column("Destination")?,
        column("Gateway")?,
        column("Flags")?,
        column("Metric")?,
        column("Mask")?,
    );
    let hex = |field: &str| {
        u32::from_str_radix(field, 16)
            .map_err(|_| format!("/proc/net/route field is not hex: '{field}'"))
    };
    let mut best: Option<(u32, Ipv4Addr)> = None;
    for line in lines.filter(|line| !line.trim().is_empty()) {
        let fields: Vec<&str> = line.split_whitespace().collect();
        if fields.len() < header.len() {
            return Err(format!(
                "/proc/net/route row has {} fields, the header {}",
                fields.len(),
                header.len()
            ));
        }
        if hex(fields[destination])? != 0
            || hex(fields[mask])? != 0
            || hex(fields[flags])? & RTF_UP_GATEWAY != RTF_UP_GATEWAY
        {
            continue;
        }
        let route_metric = fields[metric].parse::<u32>().map_err(|_| {
            format!(
                "/proc/net/route metric is not a number: '{}'",
                fields[metric]
            )
        })?;
        let address = Ipv4Addr::from(hex(fields[gateway])?.to_ne_bytes());
        if best.is_none_or(|(lowest, _)| route_metric < lowest) {
            best = Some((route_metric, address));
        }
    }
    Ok(best.map(|(_, address)| address))
}

/// How a connect attempt went: the round trip, or why it failed.
pub type Attempt = Result<Duration, String>;

/// Time a TCP connect; a refused connect is an answer (see the module docs).
pub fn tcp_connect(address: &SocketAddr, timeout: Duration) -> Attempt {
    let start = Instant::now();
    match TcpStream::connect_timeout(address, timeout) {
        Ok(_stream) => Ok(start.elapsed()),
        Err(error) if error.kind() == ErrorKind::ConnectionRefused => Ok(start.elapsed()),
        Err(error) => Err(error.to_string()),
    }
}

type Connector = Box<dyn FnMut(&SocketAddr, Duration) -> Attempt + Send>;

struct TargetSensors<'c> {
    time: DoubleBarSensor<'c>,
    failures: IntSensor<'c>,
    /// Failed attempts in the current window.
    failed: i32,
}

pub struct Reachability<'c> {
    route: PathBuf,
    internet: SocketAddr,
    gateway_port: u16,
    connect: Connector,
    gateway: TargetSensors<'c>,
    internet_sensors: TargetSensors<'c>,
    window_start: Option<Instant>,
    route_failures: FailureLog,
    post_failures: FailureLog,
}

impl<'c> Reachability<'c> {
    /// Register the four sensors (before Start). `config` was validated, so its internet target
    /// parses.
    pub fn register(
        collector: &'c Collector,
        config: &ReachabilityConfig,
        route: PathBuf,
    ) -> Result<Self, String> {
        let internet = config
            .internet_address()
            .ok_or_else(|| format!("invalid internet target '{}'", config.internet))?;
        let gateway_what = format!("the default gateway (TCP port {})", config.gateway_port);
        let internet_what = internet.to_string();
        Ok(Self {
            route,
            internet,
            gateway_port: config.gateway_port,
            connect: Box::new(tcp_connect),
            gateway: register_target(collector, Target::Gateway, &gateway_what)?,
            internet_sensors: register_target(collector, Target::Internet, &internet_what)?,
            window_start: None,
            route_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        })
    }

    /// Close the failure window if `now` ends it: the counts to post, and a new window.
    fn close_window(&mut self, now: Instant) -> Option<(i32, i32)> {
        let start = *self.window_start.get_or_insert(now);
        if now.saturating_duration_since(start) < FAILURE_WINDOW {
            return None;
        }
        self.window_start = Some(now);
        Some((
            std::mem::take(&mut self.gateway.failed),
            std::mem::take(&mut self.internet_sensors.failed),
        ))
    }

    /// One sample at `now`; `route` is the text of `/proc/net/route` (or why it is unreadable).
    pub fn sample_at(&mut self, route: Result<String, String>, now: Instant, logger: &Logger) {
        let mut posts = Ok(());
        if let Some((gateway, internet)) = self.close_window(now) {
            for (sensors, failed) in [(&self.gateway, gateway), (&self.internet_sensors, internet)]
            {
                if let Err(error) = sensors.failures.add(failed) {
                    posts = Err(error.to_string());
                }
            }
        }

        const ROUTE_WHAT: &str = "network: default gateway (/proc/net/route)";
        match route.and_then(|text| default_gateway(&text)) {
            Ok(gateway) => {
                self.route_failures.succeeded(logger, ROUTE_WHAT);
                let attempt = match gateway {
                    Some(ip) => (self.connect)(
                        &SocketAddr::new(IpAddr::V4(ip), self.gateway_port),
                        CONNECT_TIMEOUT,
                    ),
                    None => Err("no default route".to_string()),
                };
                record(&mut self.gateway, attempt, &mut posts);
            }
            Err(reason) => self.route_failures.failed(logger, ROUTE_WHAT, &reason),
        }
        let attempt = (self.connect)(&self.internet, CONNECT_TIMEOUT);
        record(&mut self.internet_sensors, attempt, &mut posts);

        match posts {
            Ok(()) => self
                .post_failures
                .succeeded(logger, "network: reachability post"),
            Err(reason) => self
                .post_failures
                .failed(logger, "network: reachability post", &reason),
        }
    }
}

fn record(sensors: &mut TargetSensors<'_>, attempt: Attempt, posts: &mut Result<(), String>) {
    match attempt {
        Ok(round_trip) => {
            if let Err(error) = sensors.time.add(round_trip.as_secs_f64() * 1000.0) {
                *posts = Err(error.to_string());
            }
        }
        Err(_) => sensors.failed = sensors.failed.saturating_add(1),
    }
}

fn register_target<'c>(
    collector: &'c Collector,
    target: Target,
    what: &str,
) -> Result<TargetSensors<'c>, String> {
    let attempts = FAILURE_WINDOW.as_secs() / PROBE_PERIOD.as_secs();
    let time_path = target_path(target, CONNECT_TIME_LEAF);
    let time = collector
        .double_bar_sensor(
            &time_path,
            BAR_PERIOD,
            BAR_POST_PERIOD,
            BAR_PRECISION,
            &SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_unit(UNIT_MILLISECONDS)
                .with_keep_history(KEEP_HISTORY)
                .with_enable_grafana(true)
                .with_description(format!(
                    "Time to open a TCP connection to {what}, ms, tried every 5 s (a refused \
                     connection is an answer). Failed attempts are not in the bar: see \
                     `Connect failures`."
                )),
        )
        .map_err(|error| format!("{time_path}: {error}"))?;
    let failures_path = target_path(target, FAILURES_LEAF);
    let failures = collector
        .int_sensor(
            &failures_path,
            &SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_unit(UNIT_COUNT)
                .with_ttl(SPEED_TTL)
                .with_keep_history(KEEP_HISTORY)
                .with_enable_grafana(true)
                .with_description(format!(
                    "TCP connections to {what} that failed in the minute, of {attempts} attempts \
                     (no answer in 2 s, network unreachable{}).",
                    if target == Target::Gateway {
                        ", no default route"
                    } else {
                        ""
                    }
                )),
        )
        .map_err(|error| format!("{failures_path}: {error}"))?;
    Ok(TargetSensors {
        time,
        failures,
        failed: 0,
    })
}

impl Source for Reachability<'_> {
    fn name(&self) -> &'static str {
        "network reachability"
    }

    fn period(&self) -> Duration {
        PROBE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        let route = std::fs::read_to_string(&self.route)
            .map_err(|error| format!("cannot read {}: {error}", self.route.display()));
        self.sample_at(route, Instant::now(), logger);
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::super::tests::{capture, network_registrations};
    use super::*;
    use crate::probe_only::docker::tests::test_collector;
    use std::net::TcpListener;
    use std::sync::{Arc, Mutex};

    /// A Wi-Fi host running Docker: two default routes (the Wi-Fi one has the lower metric), the
    /// LAN, a Docker bridge. Little-endian, as the kernel prints it on x86.
    pub const WIFI_DOCKER_ROUTE: &str = include_str!("fixtures/wifi-docker-route.txt");

    /// The reachability sensors, registered before Start when the section is enabled.
    pub const REACHABILITY_SET: &[&str] = &[
        ".computer/Network/Reachability/Gateway/Connect failures",
        ".computer/Network/Reachability/Gateway/Connect time ms",
        ".computer/Network/Reachability/Internet/Connect failures",
        ".computer/Network/Reachability/Internet/Connect time ms",
    ];

    fn enabled() -> ReachabilityConfig {
        ReachabilityConfig {
            enabled: true,
            ..ReachabilityConfig::default()
        }
    }

    #[test]
    fn the_gateway_is_the_lowest_metric_default_route() {
        assert_eq!(
            default_gateway(WIFI_DOCKER_ROUTE),
            Ok(Some(Ipv4Addr::new(192, 168, 88, 1)))
        );
        let header = WIFI_DOCKER_ROUTE.lines().next().unwrap();
        assert_eq!(default_gateway(&format!("{header}\n")), Ok(None));
        // A default route that is down is not used.
        let down =
            format!("{header}\nwlp3s0\t00000000\t0158A8C0\t0002\t0\t0\t600\t00000000\t0\t0\t0\n");
        assert_eq!(default_gateway(&down), Ok(None));
        for broken in [
            String::new(),
            "Iface\tDestination\n".to_string(),
            format!("{header}\nwlp3s0\t00000000\t0158A8C0\n"),
            format!("{header}\nwlp3s0\tXYZ\t0158A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n"),
        ] {
            assert!(default_gateway(&broken).is_err(), "{broken:?}");
        }
    }

    #[test]
    fn a_refused_connect_is_an_answer_and_a_timeout_is_not() {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let open = listener.local_addr().unwrap();
        assert!(tcp_connect(&open, CONNECT_TIMEOUT).is_ok());
        drop(listener);
        assert!(
            tcp_connect(&open, CONNECT_TIMEOUT).is_ok(),
            "nothing listens: the reset is an answer"
        );
        // TEST-NET-1 is never routed: the attempt fails (unreachable or timeout).
        let nowhere: SocketAddr = "192.0.2.1:9".parse().unwrap();
        assert!(tcp_connect(&nowhere, Duration::from_millis(200)).is_err());
    }

    #[test]
    fn the_four_sensors_register_before_start() {
        let collector = test_collector();
        Reachability::register(&collector, &enabled(), PathBuf::from("/nonexistent")).unwrap();
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
        assert_eq!(paths, REACHABILITY_SET);
        let find = |path: &str| {
            registrations
                .iter()
                .find(|json| json.contains(&format!("\"Path\":\"{path}\"")))
                .unwrap()
        };
        let time = find(REACHABILITY_SET[3]);
        for part in [
            "\"SensorType\":5,",
            "\"OriginalUnit\":1010,",
            "\"TTLTicks\":null",
            "\"KeepHistory\":77760000000000",
            "\"EnableGrafana\":true",
            "Time to open a TCP connection to 1.1.1.1:443, ms",
        ] {
            assert!(time.contains(part), "missing {part} in {time}");
        }
        let failures = find(REACHABILITY_SET[0]);
        for part in [
            "\"SensorType\":1,",
            "\"OriginalUnit\":1100,",
            "\"TTLTicks\":[3000000000]",
            "the default gateway (TCP port 53) that failed in the minute, of 12 attempts",
        ] {
            assert!(failures.contains(part), "missing {part} in {failures}");
        }
    }

    #[test]
    fn attempts_go_to_the_gateway_and_the_internet_and_failures_count_per_minute() {
        let collector = test_collector();
        let mut source =
            Reachability::register(&collector, &enabled(), PathBuf::from("/nonexistent")).unwrap();
        let tried = Arc::new(Mutex::new(Vec::new()));
        source.connect = Box::new({
            let tried = Arc::clone(&tried);
            move |address: &SocketAddr, timeout: Duration| {
                assert_eq!(timeout, CONNECT_TIMEOUT);
                tried.lock().unwrap().push(*address);
                if address.port() == 53 {
                    Ok(Duration::from_millis(4))
                } else {
                    Err("timed out".to_string())
                }
            }
        });
        let (logger, lines) = capture();
        collector.start().expect("start");
        let t0 = Instant::now();
        let at = |seconds: u64| t0 + Duration::from_secs(seconds);
        let route = || Ok(WIFI_DOCKER_ROUTE.to_string());

        source.sample_at(route(), t0, &logger);
        source.sample_at(route(), at(5), &logger);
        // The Wi-Fi association is lost: no default route, the gateway attempt fails unsent.
        let header = WIFI_DOCKER_ROUTE.lines().next().unwrap().to_string();
        source.sample_at(Ok(format!("{header}\n")), at(10), &logger);
        // An unreadable route file is not a network failure.
        source.sample_at(Err("cannot read route: EIO".into()), at(15), &logger);
        assert_eq!(
            (source.gateway.failed, source.internet_sensors.failed),
            (1, 4)
        );
        assert_eq!(
            *tried.lock().unwrap(),
            [
                "192.168.88.1:53".parse::<SocketAddr>().unwrap(),
                "1.1.1.1:443".parse().unwrap(),
                "192.168.88.1:53".parse().unwrap(),
                "1.1.1.1:443".parse().unwrap(),
                "1.1.1.1:443".parse().unwrap(),
                "1.1.1.1:443".parse().unwrap(),
            ]
        );
        // The sample that ends the minute posts the counts first, then counts itself in the next.
        source.sample_at(route(), at(60), &logger);
        assert_eq!(
            (source.gateway.failed, source.internet_sensors.failed),
            (0, 1)
        );
        assert_eq!(source.close_window(at(119)), None);
        assert_eq!(source.close_window(at(120)), Some((0, 1)));
        collector.stop().expect("stop");
        let lines = lines.lock().unwrap();
        assert_eq!(
            lines.iter().filter(|l| l.contains("EIO")).count(),
            1,
            "{lines:#?}"
        );
    }
}
