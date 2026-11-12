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
//! - **The two targets are tried at once**, each with [`CONNECT_TIMEOUT`], so a sample never takes
//!   longer than that and the probe's stop wait for its sources (2 s) holds during an outage.
//! - **For the gateway a refused connect is an answer:** the router sent a reset, so it is
//!   reachable and the time is a round trip (most home routers do not listen on every port). For
//!   the internet target it is a failure: the target is expected to listen, and a refusal there
//!   comes from a local firewall rule or a router on the way, not from the target.
//! - **A failure** is a timeout or any other error (unreachable network), and for the gateway also
//!   "no default route". Each failure episode is logged once with its reason.
//! - The gateway is the lowest-metric IPv4 default route of `/proc/net/route`, read each time, so
//!   a changed network is followed. A default route without a gateway (PPP, a VPN tunnel) has no
//!   gateway to probe: those attempts are skipped and logged once, not counted. An unreadable route
//!   file is logged once and skips the gateway attempt too. IPv6-only hosts are not supported.
//! - The time bars have no TTL: while every attempt fails they get no value, and `Connect failures`
//!   (TTL 5 min, posted every minute) shows it.

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
/// Below the probe's 2 s stop wait for its sources (both targets are tried in parallel).
pub const CONNECT_TIMEOUT: Duration = Duration::from_millis(1500);
/// The window `Connect failures` counts over.
pub const FAILURE_WINDOW: Duration = Duration::from_secs(60);
/// `Unit.Milliseconds`.
const UNIT_MILLISECONDS: i32 = 1010;
/// `Unit.Count`.
const UNIT_COUNT: i32 = 1100;

/// `RTF_UP` and `RTF_GATEWAY` (`<linux/route.h>`).
const RTF_UP: u32 = 0x0001;
const RTF_GATEWAY: u32 = 0x0002;

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

/// The host's IPv4 default route, as far as the gateway probe is concerned.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum DefaultRoute {
    /// Through a gateway: probe it.
    Via(Ipv4Addr),
    /// Straight out of a device (PPP, a VPN tunnel): there is no gateway to probe.
    PointToPoint,
    /// None at all: the network is down.
    Missing,
}

/// Parse `/proc/net/route` and pick the lowest-metric IPv4 default route that is up. Columns are
/// found by the header's names; the addresses are the kernel's `%08X` of a network-order word, so
/// its native-endian bytes are the address.
pub fn default_route(route: &str) -> Result<DefaultRoute, String> {
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
    let mut best: Option<(u32, DefaultRoute)> = None;
    for line in lines.filter(|line| !line.trim().is_empty()) {
        let fields: Vec<&str> = line.split_whitespace().collect();
        if fields.len() < header.len() {
            return Err(format!(
                "/proc/net/route row has {} fields, the header {}",
                fields.len(),
                header.len()
            ));
        }
        let route_flags = hex(fields[flags])?;
        if hex(fields[destination])? != 0 || hex(fields[mask])? != 0 || route_flags & RTF_UP == 0 {
            continue;
        }
        let route_metric = fields[metric].parse::<u32>().map_err(|_| {
            format!(
                "/proc/net/route metric is not a number: '{}'",
                fields[metric]
            )
        })?;
        let kind = if route_flags & RTF_GATEWAY != 0 {
            DefaultRoute::Via(Ipv4Addr::from(hex(fields[gateway])?.to_ne_bytes()))
        } else {
            DefaultRoute::PointToPoint
        };
        if best.is_none_or(|(lowest, _)| route_metric < lowest) {
            best = Some((route_metric, kind));
        }
    }
    Ok(best.map_or(DefaultRoute::Missing, |(_, kind)| kind))
}

/// How a connect attempt went: the round trip, or why it failed.
pub type Attempt = Result<Duration, String>;

/// Time a TCP connect. `refused_is_answer`: a reset from the target counts as reachable (see the
/// module docs).
pub fn tcp_connect(address: &SocketAddr, timeout: Duration, refused_is_answer: bool) -> Attempt {
    let start = Instant::now();
    match TcpStream::connect_timeout(address, timeout) {
        Ok(_stream) => Ok(start.elapsed()),
        Err(error) if refused_is_answer && error.kind() == ErrorKind::ConnectionRefused => {
            Ok(start.elapsed())
        }
        Err(error) => Err(error.to_string()),
    }
}

type Connector = Box<dyn Fn(&SocketAddr, Duration, bool) -> Attempt + Send + Sync>;

struct TargetSensors<'c> {
    target: Target,
    time: DoubleBarSensor<'c>,
    failures: IntSensor<'c>,
    /// Attempts and failed attempts in the current window.
    attempts: u32,
    failed: i32,
    /// The reason of the current failure episode, logged once.
    failing: Option<String>,
}

impl TargetSensors<'_> {
    /// The window's failure count to post, or `None` when the target was not tried at all in it
    /// (the gateway behind a point-to-point route or an unreadable route file): then nothing is
    /// posted and `Connect failures` goes to Timeout instead of reading a healthy 0.
    fn take_window(&mut self) -> Option<i32> {
        let failed = std::mem::take(&mut self.failed);
        (std::mem::take(&mut self.attempts) > 0).then_some(failed)
    }

    fn record(&mut self, attempt: Attempt, logger: &Logger, posts: &mut Result<(), String>) {
        self.attempts = self.attempts.saturating_add(1);
        match attempt {
            Ok(round_trip) => {
                if self.failing.take().is_some() {
                    logger.info(format!(
                        "network: reachability {}: answering again",
                        self.target.name()
                    ));
                }
                if let Err(error) = self.time.add(round_trip.as_secs_f64() * 1000.0) {
                    *posts = Err(error.to_string());
                }
            }
            Err(reason) => {
                self.failed = self.failed.saturating_add(1);
                if self.failing.as_deref() != Some(reason.as_str()) {
                    logger.warn(format!(
                        "network: reachability {}: {reason} (counted in Connect failures; \
                         repeats are not logged)",
                        self.target.name()
                    ));
                    self.failing = Some(reason);
                }
            }
        }
    }
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
    point_to_point_logged: bool,
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
            point_to_point_logged: false,
            post_failures: FailureLog::default(),
        })
    }

    /// Close the failure window if `now` ends it: the counts to post. Windows follow each other
    /// on the minute grid of the first sample; after a gap longer than a window, the grid restarts.
    fn close_window(&mut self, now: Instant) -> Option<(Option<i32>, Option<i32>)> {
        let start = *self.window_start.get_or_insert(now);
        let end = start + FAILURE_WINDOW;
        if now < end {
            return None;
        }
        self.window_start = Some(if now.saturating_duration_since(end) >= FAILURE_WINDOW {
            now
        } else {
            end
        });
        Some((
            self.gateway.take_window(),
            self.internet_sensors.take_window(),
        ))
    }

    /// The gateway address to probe, or why there is none: `Err(Some(reason))` is a failure,
    /// `Err(None)` skips the attempt.
    fn gateway_address(
        &mut self,
        route: Result<String, String>,
        logger: &Logger,
    ) -> Result<SocketAddr, Option<String>> {
        const ROUTE_WHAT: &str = "network: default gateway (/proc/net/route)";
        let route = match route.and_then(|text| default_route(&text)) {
            Ok(route) => route,
            Err(reason) => {
                self.route_failures.failed(logger, ROUTE_WHAT, &reason);
                return Err(None);
            }
        };
        self.route_failures.succeeded(logger, ROUTE_WHAT);
        match route {
            DefaultRoute::Via(ip) => {
                self.point_to_point_logged = false;
                Ok(SocketAddr::new(IpAddr::V4(ip), self.gateway_port))
            }
            DefaultRoute::PointToPoint => {
                if !self.point_to_point_logged {
                    logger.info(
                        "network: the default route has no gateway (point-to-point); the \
                         gateway is not probed while it stays so",
                    );
                    self.point_to_point_logged = true;
                }
                Err(None)
            }
            DefaultRoute::Missing => Err(Some("no default route".to_string())),
        }
    }

    /// One sample at `now`; `route` is the text of `/proc/net/route` (or why it is unreadable).
    pub fn sample_at(&mut self, route: Result<String, String>, now: Instant, logger: &Logger) {
        let mut posts = Ok(());
        if let Some((gateway, internet)) = self.close_window(now) {
            for (sensors, failed) in [(&self.gateway, gateway), (&self.internet_sensors, internet)]
            {
                let Some(failed) = failed else {
                    continue;
                };
                if let Err(error) = sensors.failures.add(failed) {
                    posts = Err(error.to_string());
                }
            }
        }

        let gateway = self.gateway_address(route, logger);
        let internet = self.internet;
        let connect = &self.connect;
        let (gateway_attempt, internet_attempt) = std::thread::scope(|scope| {
            let gateway_thread = match &gateway {
                Ok(address) => {
                    let address = *address;
                    Some(scope.spawn(move || connect(&address, CONNECT_TIMEOUT, true)))
                }
                Err(_) => None,
            };
            let internet_attempt = connect(&internet, CONNECT_TIMEOUT, false);
            let gateway_attempt = gateway_thread.map(|thread| {
                thread
                    .join()
                    .unwrap_or_else(|_| Err("the connect thread panicked".to_string()))
            });
            (gateway_attempt, internet_attempt)
        });
        match (gateway, gateway_attempt) {
            (Ok(_), Some(attempt)) => self.gateway.record(attempt, logger, &mut posts),
            (Err(Some(reason)), _) => self.gateway.record(Err(reason), logger, &mut posts),
            _ => {}
        }
        self.internet_sensors
            .record(internet_attempt, logger, &mut posts);

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

fn register_target<'c>(
    collector: &'c Collector,
    target: Target,
    what: &str,
) -> Result<TargetSensors<'c>, String> {
    let attempts = FAILURE_WINDOW.as_secs() / PROBE_PERIOD.as_secs();
    let (answer, failure_causes, normal) = match target {
        Target::Gateway => (
            " (a refused connection is an answer)",
            "no answer in 1.5 s, network unreachable, no default route",
            "On a wired LAN a few ms is normal, over Wi-Fi 2–20 ms",
        ),
        Target::Internet => (
            "",
            "no answer in 1.5 s, refused, network unreachable",
            "It depends on the target and the connection: 10–60 ms is typical for a nearby \
             target such as 1.1.1.1 from a home connection",
        ),
    };
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
                    "Time to open a TCP connection to {what}, ms, tried every 5 s{answer}: \
                     about one network round trip. **Lower is better.** {normal}; a Max of \
                     hundreds of ms is a stall of the network in that minute. Failed attempts \
                     are not in the bar: see `Connect failures`."
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
                    "TCP connections to {what} that failed in the minute, of about {attempts} \
                     attempts ({failure_causes}). **Normally 0.** A failure is either no answer \
                     within 1.5 s (a stall) or an immediate error (no route, a reject); the probe \
                     log has which. A count near the number of attempts means the target was \
                     unreachable for most of the minute."
                )),
        )
        .map_err(|error| format!("{failures_path}: {error}"))?;
    Ok(TargetSensors {
        target,
        time,
        failures,
        attempts: 0,
        failed: 0,
        failing: None,
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

    fn header() -> String {
        WIFI_DOCKER_ROUTE.lines().next().unwrap().to_string()
    }

    #[test]
    fn the_gateway_is_the_lowest_metric_default_route() {
        assert_eq!(
            default_route(WIFI_DOCKER_ROUTE),
            Ok(DefaultRoute::Via(Ipv4Addr::new(192, 168, 88, 1)))
        );
        let header = header();
        assert_eq!(
            default_route(&format!("{header}\n")),
            Ok(DefaultRoute::Missing)
        );
        // A default route that is down is not used.
        let down =
            format!("{header}\nwlp3s0\t00000000\t0158A8C0\t0002\t0\t0\t600\t00000000\t0\t0\t0\n");
        assert_eq!(default_route(&down), Ok(DefaultRoute::Missing));
        // A VPN tunnel with a lower metric than the Wi-Fi route: no gateway to probe.
        let tunnel = format!(
            "{WIFI_DOCKER_ROUTE}wg0\t00000000\t00000000\t0001\t0\t0\t50\t00000000\t0\t0\t0\n"
        );
        assert_eq!(default_route(&tunnel), Ok(DefaultRoute::PointToPoint));
        for broken in [
            String::new(),
            "Iface\tDestination\n".to_string(),
            format!("{header}\nwlp3s0\t00000000\t0158A8C0\n"),
            format!("{header}\nwlp3s0\tXYZ\t0158A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n"),
        ] {
            assert!(default_route(&broken).is_err(), "{broken:?}");
        }
    }

    #[test]
    fn a_refusal_is_an_answer_only_from_the_gateway() {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let open = listener.local_addr().unwrap();
        assert!(tcp_connect(&open, CONNECT_TIMEOUT, false).is_ok());
        drop(listener);
        assert!(
            tcp_connect(&open, CONNECT_TIMEOUT, true).is_ok(),
            "nothing listens: the gateway's reset is an answer"
        );
        assert!(
            tcp_connect(&open, CONNECT_TIMEOUT, false).is_err(),
            "the internet target must accept"
        );
        // TEST-NET-1 is never routed: the attempt fails (unreachable or timeout).
        let nowhere: SocketAddr = "192.0.2.1:9".parse().unwrap();
        assert!(tcp_connect(&nowhere, Duration::from_millis(200), true).is_err());
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
            "Time to open a TCP connection to 1.1.1.1:443, ms, tried every 5 s: about",
            "10–60 ms is typical",
        ] {
            assert!(time.contains(part), "missing {part} in {time}");
        }
        let failures = find(REACHABILITY_SET[0]);
        for part in [
            "\"SensorType\":1,",
            "\"OriginalUnit\":1100,",
            "\"TTLTicks\":[3000000000]",
            "the default gateway (TCP port 53) that failed in the minute, of about 12 attempts",
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
            move |address: &SocketAddr, timeout: Duration, refused_is_answer: bool| {
                assert_eq!(timeout, CONNECT_TIMEOUT);
                tried.lock().unwrap().push((*address, refused_is_answer));
                if address.port() == 53 {
                    Ok(Duration::from_millis(4))
                } else {
                    Err("connection timed out".to_string())
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
        // The Wi-Fi association is lost: no default route, a gateway failure with no attempt.
        source.sample_at(Ok(format!("{}\n", header())), at(10), &logger);
        // An unreadable route file is not a network failure, nor is a point-to-point route.
        source.sample_at(Err("cannot read route: EIO".into()), at(15), &logger);
        let tunnel = format!(
            "{}\nwg0\t00000000\t00000000\t0001\t0\t0\t0\t00000000\t0\t0\t0\n",
            header()
        );
        source.sample_at(Ok(tunnel.clone()), at(20), &logger);
        source.sample_at(Ok(tunnel), at(25), &logger);
        assert_eq!(
            (source.gateway.failed, source.internet_sensors.failed),
            (1, 6)
        );
        let mut tried = tried.lock().unwrap().clone();
        tried.sort();
        let gateway = ("192.168.88.1:53".parse::<SocketAddr>().unwrap(), true);
        let internet = ("1.1.1.1:443".parse::<SocketAddr>().unwrap(), false);
        assert_eq!(
            tried,
            [internet, internet, internet, internet, internet, internet, gateway, gateway]
        );

        // The sample that ends the minute posts the counts first, then counts itself in the next.
        source.sample_at(route(), at(60), &logger);
        assert_eq!(
            (source.gateway.failed, source.internet_sensors.failed),
            (0, 1)
        );
        // The next window ends on the grid (120 s), not 60 s after a late sample.
        assert_eq!(source.close_window(at(119)), None);
        assert_eq!(source.close_window(at(121)), Some((Some(0), Some(1))));
        assert_eq!(source.close_window(at(179)), None);
        // A window with no attempt at all posts nothing (a healthy 0 would hide the gap).
        assert_eq!(source.close_window(at(180)), Some((None, None)));
        // After a long gap the grid restarts.
        assert_eq!(source.close_window(at(400)), Some((None, None)));
        assert_eq!(source.close_window(at(459)), None);

        // A whole window behind a VPN tunnel: the gateway is never tried, so its count is not
        // posted, while the internet's is.
        let tunnel = format!(
            "{}\nwg0\t00000000\t00000000\t0001\t0\t0\t0\t00000000\t0\t0\t0\n",
            header()
        );
        for seconds in (460..520).step_by(5) {
            source.sample_at(Ok(tunnel.clone()), at(seconds), &logger);
        }
        assert_eq!(source.close_window(at(520)), Some((None, Some(12))));
        collector.stop().expect("stop");

        let lines = lines.lock().unwrap();
        let count = |text: &str| lines.iter().filter(|l| l.contains(text)).count();
        assert_eq!(count("EIO"), 1, "{lines:#?}");
        assert_eq!(count("point-to-point"), 2, "once per episode: {lines:#?}");
        assert_eq!(count("Internet: connection timed out"), 1, "{lines:#?}");
        assert_eq!(count("Gateway: no default route"), 1, "{lines:#?}");
        assert_eq!(count("Gateway: answering again"), 1, "{lines:#?}");
    }
}
