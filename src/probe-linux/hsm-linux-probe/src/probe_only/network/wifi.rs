//! Wi-Fi signal (#1711 group 3), per reported Wi-Fi interface, from `/proc/net/wireless`:
//!
//! | leaf (`.computer/Network/<iface>/…`) | `/proc/net/wireless` | meaning |
//! |---|---|---|
//! | `Signal dBm` | `level` | the access point's signal at the card, dBm |
//! | `Link quality` | `link` | 0–70 on cfg80211 drivers: the signal clamped to −110…−40 dBm, plus 110 |
//!
//! Both are 1-minute DoubleBars of samples taken every [`SAMPLE_PERIOD`], so a dip shows in the
//! bar's Min. A sensor is created with its first value. The kernel prints a row only while the
//! interface is associated, so a disconnected minute has no value (`Link up` tells why).
//!
//! Only these two. The access point (BSSID), channel and channel width are reported only by
//! nl80211 (generic netlink); by the owner's decision in #1711 the probe does not add a netlink
//! dependency for them, and protocol code is never hand-written here.

use std::collections::BTreeMap;
use std::io::ErrorKind;
use std::path::PathBuf;
use std::time::Duration;

use hsm_collector::{Collector, DoubleBarSensor, SensorOptions};

use super::{
    interface_path, procfs, report, InterfaceCap, InterfaceFilter, BAR_PERIOD, BAR_POST_PERIOD,
    BAR_PRECISION, KEEP_HISTORY, SAMPLE_PERIOD, SPEED_TTL,
};
use crate::logging::Logger;
use crate::probe_only::{FailureLog, Source};

pub const SIGNAL_LEAF: &str = "Signal dBm";
pub const QUALITY_LEAF: &str = "Link quality";

#[derive(Clone, Copy, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub enum Reading {
    Signal,
    Quality,
}

impl Reading {
    pub fn leaf(self) -> &'static str {
        match self {
            Reading::Signal => SIGNAL_LEAF,
            Reading::Quality => QUALITY_LEAF,
        }
    }

    pub fn description(self, interface: &str) -> String {
        match self {
            Reading::Signal => format!(
                "Signal of the access point on Wi-Fi interface **{interface}**, dBm \
                 (`/proc/net/wireless` level): about −50 is strong, below −75 unreliable. \
                 Sampled every 10 s; the bar's Min shows a dip."
            ),
            Reading::Quality => format!(
                "Link quality of Wi-Fi interface **{interface}** (`/proc/net/wireless` link): \
                 0–70 on cfg80211 drivers, the signal clamped to −110…−40 dBm plus 110. \
                 Sampled every 10 s."
            ),
        }
    }
}

pub struct WifiSignal<'c> {
    collector: &'c Collector,
    wireless: PathBuf,
    filter: InterfaceFilter,
    sensors: BTreeMap<(String, Reading), DoubleBarSensor<'c>>,
    cap: InterfaceCap,
    read_failures: FailureLog,
    registration_failures: FailureLog,
    post_failures: FailureLog,
}

const WIFI_WHAT: &str = "network: Wi-Fi signal (/proc/net/wireless)";

impl<'c> WifiSignal<'c> {
    pub fn new(collector: &'c Collector, filter: InterfaceFilter, wireless: PathBuf) -> Self {
        Self {
            collector,
            wireless,
            filter,
            sensors: BTreeMap::new(),
            cap: InterfaceCap::new("Wi-Fi"),
            read_failures: FailureLog::default(),
            registration_failures: FailureLog::default(),
            post_failures: FailureLog::default(),
        }
    }

    fn read(&self) -> Result<Option<String>, String> {
        match std::fs::read_to_string(&self.wireless) {
            Ok(text) => Ok(Some(text)),
            // A kernel without wireless extensions in procfs (a cloud kernel) has no file; on a
            // host without Wi-Fi that is normal, not a failure.
            Err(error)
                if error.kind() == ErrorKind::NotFound
                    && !procfs::any_wireless(self.filter.sys_root()) =>
            {
                Ok(None)
            }
            Err(error) => Err(format!("cannot read {}: {error}", self.wireless.display())),
        }
    }

    /// One sample: `text` is `/proc/net/wireless` (`None`: absent on a host without Wi-Fi).
    pub fn sample_text(&mut self, text: Result<Option<String>, String>, logger: &Logger) {
        let rows = match text.and_then(|text| text.map(|t| procfs::parse_wireless(&t)).transpose())
        {
            Ok(Some(rows)) => rows,
            Ok(None) => return,
            Err(reason) => {
                self.read_failures.failed(logger, WIFI_WHAT, &reason);
                return;
            }
        };
        self.read_failures.succeeded(logger, WIFI_WHAT);

        let mut registrations = Ok(());
        let mut posts = Ok(());
        let reported: Vec<_> = rows
            .into_iter()
            .filter(|(name, _)| self.filter.reported(name))
            .collect();
        for (name, row) in &reported {
            for (reading, value) in [
                (Reading::Signal, row.signal_dbm),
                (Reading::Quality, row.quality),
            ] {
                if let Some(value) = value {
                    self.post(name, reading, value, logger, &mut registrations, &mut posts);
                }
            }
        }
        report(
            &mut self.registration_failures,
            logger,
            "network: Wi-Fi registration",
            registrations,
        );
        report(
            &mut self.post_failures,
            logger,
            "network: Wi-Fi post",
            posts,
        );
    }

    fn post(
        &mut self,
        interface: &str,
        reading: Reading,
        value: f64,
        logger: &Logger,
        registrations: &mut Result<(), String>,
        posts: &mut Result<(), String>,
    ) {
        let key = (interface.to_string(), reading);
        let path = interface_path(interface, reading.leaf());
        if !self.sensors.contains_key(&key) {
            if !self.cap.admit(interface, logger) {
                return;
            }
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_keep_history(KEEP_HISTORY)
                .with_ttl(SPEED_TTL)
                .with_enable_grafana(true)
                .with_description(reading.description(interface));
            match self.collector.double_bar_sensor(
                &path,
                BAR_PERIOD,
                BAR_POST_PERIOD,
                BAR_PRECISION,
                &options,
            ) {
                Ok(sensor) => {
                    self.sensors.insert(key.clone(), sensor);
                }
                Err(error) => {
                    *registrations = Err(format!("{path}: {error}"));
                    return;
                }
            }
        }
        if let Some(sensor) = self.sensors.get(&key) {
            if let Err(error) = sensor.add(value) {
                *posts = Err(format!("{path}: {error}"));
            }
        }
    }
}

impl Source for WifiSignal<'_> {
    fn name(&self) -> &'static str {
        "network wifi"
    }

    fn period(&self) -> Duration {
        SAMPLE_PERIOD
    }

    fn sample(&mut self, logger: &Logger) {
        let text = self.read();
        self.sample_text(text, logger);
    }
}

#[cfg(test)]
mod tests {
    use super::super::procfs::tests::{wifi_docker_sysfs, WIFI_WIRELESS};
    use super::super::tests::{capture, network_registrations, test_filter};
    use super::*;
    use crate::config::NetworkConfig;
    use crate::probe_only::docker::tests::test_collector;
    use crate::probe_only::host::tests::FakeTree;

    fn wifi_source<'c>(
        collector: &'c Collector,
        tree: &FakeTree,
        patterns: &[&str],
    ) -> WifiSignal<'c> {
        let config = NetworkConfig {
            enabled: true,
            interfaces: patterns.iter().map(|p| p.to_string()).collect(),
        };
        WifiSignal::new(
            collector,
            test_filter(&config, tree),
            tree.0.join("proc-net/wireless"),
        )
    }

    #[test]
    fn an_associated_interface_gets_signal_and_quality_bars() {
        let collector = test_collector();
        let tree = FakeTree::new("net-wifi");
        wifi_docker_sysfs(&tree);
        tree.file("proc-net/wireless", WIFI_WIRELESS);
        let mut source = wifi_source(&collector, &tree, &[]);
        let (logger, lines) = capture();
        collector.start().expect("start");
        source.sample(&logger);
        let registrations = network_registrations(&collector);
        collector.stop().expect("stop");

        // Only wlp3s0 is a physical interface of this host; the other rows are not reported.
        assert_eq!(registrations.len(), 2, "{registrations:#?}");
        let find = |leaf: &str| {
            registrations
                .iter()
                .find(|json| {
                    json.contains(&format!("\"Path\":\".computer/Network/wlp3s0/{leaf}\""))
                })
                .unwrap_or_else(|| panic!("no {leaf} in {registrations:#?}"))
        };
        for (leaf, description) in [
            (
                SIGNAL_LEAF,
                "Signal of the access point on Wi-Fi interface **wlp3s0**, dBm",
            ),
            (QUALITY_LEAF, "Link quality of Wi-Fi interface **wlp3s0**"),
        ] {
            let json = find(leaf);
            for part in [
                "\"SensorType\":5,",
                "\"TTLTicks\":[3000000000]",
                "\"KeepHistory\":77760000000000",
                "\"EnableGrafana\":true",
                "\"IsSingletonSensor\":true",
                description,
            ] {
                assert!(json.contains(part), "missing {part} in {json}");
            }
        }
        assert!(lines.lock().unwrap().is_empty(), "{lines:#?}");
    }

    #[test]
    fn a_pattern_reports_a_dongle_without_a_dbm_level_as_quality_only() {
        let collector = test_collector();
        let tree = FakeTree::new("net-wifi-dongle");
        tree.file("proc-net/wireless", WIFI_WIRELESS);
        let mut source = wifi_source(&collector, &tree, &["wlx*", "wlan*"]);
        let (logger, _) = capture();
        collector.start().expect("start");
        source.sample(&logger);
        let registrations = network_registrations(&collector);
        collector.stop().expect("stop");
        assert_eq!(registrations.len(), 1, "{registrations:#?}");
        assert!(registrations[0].contains("\"Path\":\".computer/Network/wlx00c0ca/Link quality\""));
    }

    #[test]
    fn a_missing_file_is_silent_without_wifi_and_logged_once_with_it() {
        let collector = test_collector();
        let tree = FakeTree::new("net-wifi-missing");
        let mut source = wifi_source(&collector, &tree, &[]);
        let (logger, lines) = capture();
        source.sample(&logger);
        source.sample(&logger);
        assert!(
            lines.lock().unwrap().is_empty(),
            "no Wi-Fi interface: silent"
        );

        wifi_docker_sysfs(&tree);
        source.sample(&logger);
        source.sample(&logger);
        tree.file("proc-net/wireless", WIFI_WIRELESS);
        collector.start().expect("start");
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
    }
}
