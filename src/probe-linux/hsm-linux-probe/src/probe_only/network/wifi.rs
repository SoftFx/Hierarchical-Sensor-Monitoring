//! Wi-Fi signal (#1711 group 3), per reported Wi-Fi interface, from `/proc/net/wireless`:
//! `.computer/Network/<iface>/Signal dBm`, the access point's signal at the card (the `level`
//! column, dBm only).
//!
//! A 1-minute DoubleBar of samples taken every [`SAMPLE_PERIOD`], so a dip shows in the bar's
//! Min. The sensor is created with its first value. The kernel prints a row only while the
//! interface is associated, so a disconnected minute has no value (`Link up` tells why); that is
//! also why it has no TTL: a long disconnect would otherwise put it in Timeout.
//!
//! Only the signal. The `link` quality column is left out by the owner's decision (#1731): on
//! cfg80211 drivers it is the signal plus 110, clamped to 0–70, so it repeats `Signal dBm`. The
//! access point (BSSID), channel and channel width are reported only by nl80211 (generic
//! netlink); by the owner's decision in #1711 the probe does not add a netlink dependency for
//! them, and protocol code is never hand-written here.

use std::collections::BTreeMap;
use std::io::ErrorKind;
use std::path::PathBuf;
use std::time::Duration;

use hsm_collector::{Collector, DoubleBarSensor, SensorOptions};

use super::{
    interface_path, procfs, report, InterfaceCap, InterfaceFilter, BAR_PERIOD, BAR_POST_PERIOD,
    BAR_PRECISION, KEEP_HISTORY, SAMPLE_PERIOD,
};
use crate::logging::Logger;
use crate::probe_only::{FailureLog, Source};

pub const SIGNAL_LEAF: &str = "Signal dBm";

pub fn signal_description(interface: &str) -> String {
    format!(
        "Signal of the access point on Wi-Fi interface **{interface}**, dBm \
         (`/proc/net/wireless` level): how strongly the router's radio reaches this card. Always \
         negative; **the closer to 0, the better**. Every 10 dB is ten times less power, so −70 \
         is ten times weaker than −60.\n\n\
         - −30 … −50: excellent\n\
         - −50 … −60: good\n\
         - −60 … −67: fine, enough for calls and video\n\
         - −67 … −75: weak: works, but retries and short stalls begin\n\
         - below −75: unreliable: stalls and disconnects are likely\n\n\
         Sampled every 10 s into 1-minute bars: **Min** is the weakest of the minute's samples, \
         so a dip shows there even when Mean looks fine (a dip shorter than 10 s can fall \
         between samples)."
    )
}

pub struct WifiSignal<'c> {
    collector: &'c Collector,
    wireless: PathBuf,
    filter: InterfaceFilter,
    sensors: BTreeMap<String, DoubleBarSensor<'c>>,
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
            // A kernel without wireless extensions in procfs (a cloud kernel) has no file; with no
            // reported Wi-Fi interface that is normal, not a failure.
            Err(error)
                if error.kind() == ErrorKind::NotFound
                    && !procfs::wireless_interfaces(self.filter.sys_root())
                        .iter()
                        .any(|name| self.filter.reported(name)) =>
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
            if let Some(dbm) = row.signal_dbm {
                self.post(name, dbm, logger, &mut registrations, &mut posts);
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
        dbm: f64,
        logger: &Logger,
        registrations: &mut Result<(), String>,
        posts: &mut Result<(), String>,
    ) {
        let path = interface_path(interface, SIGNAL_LEAF);
        if !self.sensors.contains_key(interface) {
            if !self.cap.admit(interface, logger) {
                return;
            }
            let options = SensorOptions::default()
                .with_is_computer_sensor(true)
                .with_keep_history(KEEP_HISTORY)
                .with_enable_grafana(true)
                .with_description(signal_description(interface));
            match self.collector.double_bar_sensor(
                &path,
                BAR_PERIOD,
                BAR_POST_PERIOD,
                BAR_PRECISION,
                &options,
            ) {
                Ok(sensor) => {
                    self.sensors.insert(interface.to_string(), sensor);
                }
                Err(error) => {
                    *registrations = Err(format!("{path}: {error}"));
                    return;
                }
            }
        }
        if let Some(sensor) = self.sensors.get(interface) {
            if let Err(error) = sensor.add(dbm) {
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
            ..NetworkConfig::default()
        };
        WifiSignal::new(
            collector,
            test_filter(&config, tree),
            tree.0.join("proc-net/wireless"),
        )
    }

    #[test]
    fn an_associated_interface_gets_one_signal_bar() {
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

        // Only wlp3s0 is a physical interface of this host; the other rows are not reported. No
        // `Link quality` (#1731): it repeats the signal.
        assert_eq!(registrations.len(), 1, "{registrations:#?}");
        let json = &registrations[0];
        for part in [
            "\"Path\":\".computer/Network/wlp3s0/Signal dBm\"",
            "\"SensorType\":5,",
            "\"TTLTicks\":null",
            "\"KeepHistory\":77760000000000",
            "\"EnableGrafana\":true",
            "\"IsSingletonSensor\":true",
            "Signal of the access point on Wi-Fi interface **wlp3s0**, dBm",
            // The scale is a markdown list: the newlines travel JSON-escaped.
            "\\n- −30 … −50: excellent\\n",
        ] {
            assert!(json.contains(part), "missing {part} in {json}");
        }
        assert!(lines.lock().unwrap().is_empty(), "{lines:#?}");
    }

    #[test]
    fn patterns_admit_every_row_but_only_a_fresh_dbm_level_gets_a_sensor() {
        let collector = test_collector();
        let tree = FakeTree::new("net-wifi-dongle");
        tree.file("proc-net/wireless", WIFI_WIRELESS);
        // No sysfs: the patterns alone decide, and they admit all four rows.
        let mut source = wifi_source(&collector, &tree, &["wl*"]);
        let (logger, _) = capture();
        collector.start().expect("start");
        source.sample(&logger);
        let registrations = network_registrations(&collector);
        collector.stop().expect("stop");
        // wlx00c0ca (non-dBm level), wlan9 (nothing updated) and wlan8 (dBm level not marked
        // updated) get none.
        assert_eq!(registrations.len(), 1, "{registrations:#?}");
        assert!(registrations[0].contains("\"Path\":\".computer/Network/wlp3s0/Signal dBm\""));
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
        let mut excluded = wifi_source(&collector, &tree, &["enp*"]);
        excluded.sample(&logger);
        assert!(
            lines.lock().unwrap().is_empty(),
            "Wi-Fi not reported: silent"
        );
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
