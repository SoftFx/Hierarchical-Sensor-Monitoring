//! The kernel's network counters, parsed from their text: `/proc/net/snmp` (the TCP MIB) and
//! `/proc/net/dev` (per-interface octets), plus which interfaces are reported and the speed rule.
//! Pure functions over the file text, tested on captured files (`fixtures/`).

use std::collections::BTreeMap;
use std::path::Path;
use std::time::Duration;

/// The three RFC 1213 TCP-group counters the Windows sensors read through PDH (`\TCPv4` +
/// `\TCPv6`). Linux keeps one TCP MIB for both address families (`/proc/net/snmp6` has no TCP
/// row), so these are already the IPv4 + IPv6 sum.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct TcpCounters {
    /// `CurrEstab` (`tcpCurrEstab`): connections now ESTABLISHED or CLOSE-WAIT — a gauge.
    pub curr_estab: u64,
    /// `AttemptFails` (`tcpAttemptFails`): cumulative failed connection attempts.
    pub attempt_fails: u64,
    /// `EstabResets` (`tcpEstabResets`): cumulative resets of established connections.
    pub estab_resets: u64,
}

/// Parse the `Tcp:` header/value row pair of `/proc/net/snmp`, by column name (the kernel has
/// appended columns before, so positions are not relied on).
pub fn parse_tcp(snmp: &str) -> Result<TcpCounters, String> {
    let mut rows = snmp.lines().filter(|line| line.starts_with("Tcp:"));
    let header = rows.next().ok_or("no Tcp: row in /proc/net/snmp")?;
    let values = rows
        .next()
        .ok_or("the Tcp: header row of /proc/net/snmp has no value row")?;
    let names: Vec<&str> = header.split_whitespace().skip(1).collect();
    let fields: Vec<&str> = values.split_whitespace().skip(1).collect();
    if names.len() != fields.len() {
        return Err(format!(
            "the Tcp: rows of /proc/net/snmp disagree: {} names, {} values",
            names.len(),
            fields.len()
        ));
    }
    let counter = |name: &str| -> Result<u64, String> {
        let index = names
            .iter()
            .position(|column| *column == name)
            .ok_or_else(|| format!("no Tcp {name} column in /proc/net/snmp"))?;
        fields[index]
            .parse::<u64>()
            .map_err(|_| format!("Tcp {name} is not a counter: '{}'", fields[index]))
    };
    Ok(TcpCounters {
        curr_estab: counter("CurrEstab")?,
        attempt_fails: counter("AttemptFails")?,
        estab_resets: counter("EstabResets")?,
    })
}

/// The change of a cumulative counter since the previous read, as the Windows delta sensors post
/// it (managed `ConnectionsDifferenceSensor`, native `PdhReadDelta`): nothing on the first read
/// (the baseline), nothing when it did not move (a quiet minute posts no value), and nothing when
/// it went down (a reset; the caller keeps the new reading as the next baseline).
pub fn counter_delta(previous: Option<u64>, current: u64) -> Option<i32> {
    let delta = current.checked_sub(previous?)?;
    (delta > 0).then(|| i32::try_from(delta).unwrap_or(i32::MAX))
}

/// One interface's `/proc/net/dev` counters: octets (columns 1 and 9) for the speed, and the
/// receive + transmit sums of `errs` (columns 3 and 11) and `drop` (columns 4 and 12) for health.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct DevCounters {
    pub rx_bytes: u64,
    pub tx_bytes: u64,
    pub errors: u64,
    pub drops: u64,
}

/// The 16 counter columns of a `/proc/net/dev` row (8 receive, 8 transmit).
const DEV_COLUMNS: usize = 16;
const RX_ERRS_COLUMN: usize = 2;
const RX_DROP_COLUMN: usize = 3;
const TX_BYTES_COLUMN: usize = 8;
const TX_ERRS_COLUMN: usize = 10;
const TX_DROP_COLUMN: usize = 11;

/// Parse `/proc/net/dev`: two header lines, then `<name>:<16 counters>` per interface. The kernel
/// pads the name to 6 characters and the first counter to 8, so a long name or a large counter
/// leaves no space after the `:` (`wlp3s0:18446744073 …`) — the row is split at the colon, which
/// an interface name cannot contain.
pub fn parse_dev(dev: &str) -> Result<BTreeMap<String, DevCounters>, String> {
    let mut lines = dev.lines();
    match lines.next() {
        Some(header) if header.starts_with("Inter-|") => {}
        _ => return Err("/proc/net/dev does not start with its 'Inter-|' header".into()),
    }
    lines.next();
    let mut interfaces = BTreeMap::new();
    for line in lines.filter(|line| !line.trim().is_empty()) {
        let (name, counters) = line
            .split_once(':')
            .ok_or_else(|| format!("/proc/net/dev row without a name: '{}'", line.trim()))?;
        let name = name.trim();
        let fields: Vec<&str> = counters.split_whitespace().collect();
        if name.is_empty() || fields.len() < DEV_COLUMNS {
            return Err(format!(
                "/proc/net/dev row for '{name}' has {} counters, expected {DEV_COLUMNS}",
                fields.len()
            ));
        }
        let column = |index: usize| -> Result<u64, String> {
            fields[index].parse::<u64>().map_err(|_| {
                format!(
                    "/proc/net/dev column {} of '{name}' is not a counter: '{}'",
                    index + 1,
                    fields[index]
                )
            })
        };
        interfaces.insert(
            name.to_string(),
            DevCounters {
                rx_bytes: column(0)?,
                tx_bytes: column(TX_BYTES_COLUMN)?,
                errors: column(RX_ERRS_COLUMN)?.saturating_add(column(TX_ERRS_COLUMN)?),
                drops: column(RX_DROP_COLUMN)?.saturating_add(column(TX_DROP_COLUMN)?),
            },
        );
    }
    Ok(interfaces)
}

/// `ARPHRD_LOOPBACK`, the `type` of a loopback interface.
const ARPHRD_LOOPBACK: &str = "772";

/// Whether the interface is backed by a device (`/sys/class/net/<if>/device` exists): a NIC,
/// Wi-Fi card, USB adapter. Loopback, bridges (`docker0`, `br-…`), `veth` pairs, tunnels and
/// bonds are virtual and have none.
pub fn is_physical(sys_root: &Path, name: &str) -> bool {
    sys_root
        .join("class/net")
        .join(name)
        .join("device")
        .symlink_metadata()
        .is_ok()
}

/// Whether the interface is a loopback one (by `type`, else by the name `lo`). Never reported,
/// as on Windows (`IF_TYPE_SOFTWARE_LOOPBACK`), even when a pattern names it.
pub fn is_loopback(sys_root: &Path, name: &str) -> bool {
    match std::fs::read_to_string(sys_root.join("class/net").join(name).join("type")) {
        Ok(kind) => kind.trim() == ARPHRD_LOOPBACK,
        Err(_) => name == "lo",
    }
}

fn interface_file(sys_root: &Path, name: &str, file: &str) -> Result<String, String> {
    let path = sys_root.join("class/net").join(name).join(file);
    std::fs::read_to_string(&path)
        .map(|text| text.trim().to_string())
        .map_err(|error| format!("cannot read {}: {error}", path.display()))
}

/// Whether the interface has a link, from its RFC 2863 `operstate`: `up` is a link; `down`,
/// `lowerlayerdown`, `dormant` (Wi-Fi not associated), `notpresent` and `testing` are not.
/// Drivers that do not track it report `unknown`; then the `carrier` file decides.
pub fn link_up(sys_root: &Path, name: &str) -> Result<bool, String> {
    let state = interface_file(sys_root, name, "operstate")?;
    match state.as_str() {
        "up" => Ok(true),
        "down" | "lowerlayerdown" | "dormant" | "notpresent" | "testing" => Ok(false),
        "unknown" => match interface_file(sys_root, name, "carrier")?.as_str() {
            "1" => Ok(true),
            "0" => Ok(false),
            other => Err(format!("{name}: unexpected carrier '{other}'")),
        },
        other => Err(format!("{name}: unexpected operstate '{other}'")),
    }
}

/// `carrier_changes`: how many times the link went up or down since the interface was created (a
/// flap counts 2). `None` when the kernel has no such file (before 3.15) or it is unreadable.
pub fn carrier_changes(sys_root: &Path, name: &str) -> Option<u64> {
    interface_file(sys_root, name, "carrier_changes")
        .ok()?
        .parse()
        .ok()
}

/// Whether the interface is a Wi-Fi one (cfg80211 creates `/sys/class/net/<if>/wireless`).
pub fn is_wireless(sys_root: &Path, name: &str) -> bool {
    sys_root
        .join("class/net")
        .join(name)
        .join("wireless")
        .is_dir()
}

/// The host's Wi-Fi interfaces.
pub fn wireless_interfaces(sys_root: &Path) -> Vec<String> {
    std::fs::read_dir(sys_root.join("class/net"))
        .map(|entries| {
            entries
                .flatten()
                .map(|entry| entry.file_name().to_string_lossy().into_owned())
                .filter(|name| is_wireless(sys_root, name))
                .collect()
        })
        .unwrap_or_default()
}

/// One associated Wi-Fi interface's row of `/proc/net/wireless`.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct WirelessReading {
    /// `link`: 0–70 on cfg80211 drivers (the signal clamped to −110…−40 dBm, plus 110).
    pub quality: Option<f64>,
    /// `level` in dBm.
    pub signal_dbm: Option<f64>,
}

/// Parse `/proc/net/wireless`. The kernel (`wireless_seq_printf_stats`) prints a row only for an
/// interface that has statistics — for cfg80211, a station associated with an access point — as
/// `%6s: %04x  %3d%c  %3d%c  %3d%c …`: status, then link quality, level and noise, each followed
/// by `.` when the driver updated it and a space when it did not. A value without its `.` is
/// stale or invalid and is left out; so is a level that is not in dBm (the kernel prints a dBm
/// level as a negative number, any other scale as 0–255).
pub fn parse_wireless(text: &str) -> Result<BTreeMap<String, WirelessReading>, String> {
    let mut lines = text.lines();
    match lines.next() {
        Some(header) if header.starts_with("Inter-|") => {}
        _ => return Err("/proc/net/wireless does not start with its 'Inter-|' header".into()),
    }
    lines.next();
    let mut interfaces = BTreeMap::new();
    for line in lines.filter(|line| !line.trim().is_empty()) {
        let (name, fields) = line
            .split_once(':')
            .ok_or_else(|| format!("/proc/net/wireless row without a name: '{}'", line.trim()))?;
        let name = name.trim();
        let fields: Vec<&str> = fields.split_whitespace().collect();
        if name.is_empty() || fields.len() < 3 {
            return Err(format!(
                "/proc/net/wireless row for '{name}' has {} fields, expected at least 3",
                fields.len()
            ));
        }
        let value = |index: usize| -> Result<Option<i32>, String> {
            let field = fields[index];
            let (digits, updated) = match field.strip_suffix('.') {
                Some(digits) => (digits, true),
                None => (field, false),
            };
            let number = digits.parse::<i32>().map_err(|_| {
                format!("/proc/net/wireless field {index} of '{name}' is not a number: '{field}'")
            })?;
            Ok(updated.then_some(number))
        };
        let quality = value(1)?;
        let level = value(2)?;
        interfaces.insert(
            name.to_string(),
            WirelessReading {
                quality: quality.map(f64::from),
                signal_dbm: level.filter(|dbm| *dbm < 0).map(f64::from),
            },
        );
    }
    Ok(interfaces)
}

/// One interface's speed over an interval, MB/s (1 MB = 1024² bytes, as on Windows).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Speed {
    pub rx_mb_per_sec: f64,
    pub tx_mb_per_sec: f64,
}

const BYTES_PER_MB: f64 = 1024.0 * 1024.0;

/// The Windows sampler's rule (`WindowsNetworkSampler::Sample`): an interface seen for the first
/// time gives no speed (it appears next interval); one whose counter went down (a reset, an
/// interface re-created under the same name) is skipped for this interval; one that moved no
/// octet at all gives no speed, so a perpetually idle interface never gets a sensor.
pub fn speeds(
    previous: &BTreeMap<String, DevCounters>,
    current: &BTreeMap<String, DevCounters>,
    elapsed: Duration,
) -> BTreeMap<String, Speed> {
    let seconds = elapsed.as_secs_f64();
    let mut result = BTreeMap::new();
    if seconds <= 0.0 {
        return result;
    }
    for (name, now) in current {
        let Some(before) = previous.get(name) else {
            continue;
        };
        let (Some(rx), Some(tx)) = (
            now.rx_bytes.checked_sub(before.rx_bytes),
            now.tx_bytes.checked_sub(before.tx_bytes),
        ) else {
            continue;
        };
        if rx == 0 && tx == 0 {
            continue;
        }
        result.insert(
            name.clone(),
            Speed {
                rx_mb_per_sec: rx as f64 / BYTES_PER_MB / seconds,
                tx_mb_per_sec: tx as f64 / BYTES_PER_MB / seconds,
            },
        );
    }
    result
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::probe_only::host::tests::FakeTree;

    /// Captured from a WSL 2 kernel (5.15): loopback, one NIC, an idle `docker0`.
    pub const WSL_SNMP: &str = include_str!("fixtures/wsl-snmp.txt");
    pub const WSL_DEV: &str = include_str!("fixtures/wsl-dev.txt");
    /// A Wi-Fi host running Docker, in the kernel's exact `/proc/net/dev` format (`%6s:%8llu …`):
    /// counters wider than their column run into the name, as they do on a long-running host.
    pub const WIFI_DOCKER_DEV: &str = include_str!("fixtures/wifi-docker-dev.txt");

    /// `/proc/net/wireless` of the same host, in the kernel's exact format: `wlp3s0` associated at
    /// −74 dBm, a USB dongle whose driver reports a non-dBm level, and a row with nothing updated.
    pub const WIFI_WIRELESS: &str = include_str!("fixtures/wifi-wireless.txt");

    /// `/sys/class/net` of [`WIFI_DOCKER_DEV`]: only the two NICs have a `device` link and only
    /// `wlp3s0` is Wi-Fi; it is up, the Ethernet port has no cable.
    pub fn wifi_docker_sysfs(tree: &FakeTree) {
        for (name, kind, physical, operstate) in [
            ("lo", "772", false, "unknown"),
            ("wlp3s0", "1", true, "up"),
            ("enp2s0", "1", true, "down"),
            ("docker0", "1", false, "up"),
            ("br-5f1c2a9e0b7d", "1", false, "down"),
            ("veth1a2b3c4", "1", false, "up"),
        ] {
            tree.file(&format!("sys/class/net/{name}/type"), &format!("{kind}\n"));
            tree.file(
                &format!("sys/class/net/{name}/operstate"),
                &format!("{operstate}\n"),
            );
            tree.file(&format!("sys/class/net/{name}/carrier_changes"), "2\n");
            if physical {
                tree.file(&format!("sys/class/net/{name}/device/uevent"), "");
            }
        }
        tree.file("sys/class/net/wlp3s0/wireless/.keep", "");
    }

    #[test]
    fn tcp_counters_are_read_by_column_name() {
        assert_eq!(
            parse_tcp(WSL_SNMP),
            Ok(TcpCounters {
                curr_estab: 0,
                attempt_fails: 0,
                estab_resets: 0
            })
        );
        // Columns in another order, one appended: still found by name.
        let snmp = "Ip: Forwarding\nIp: 1\n\
                    Tcp: CurrEstab RtoAlgorithm EstabResets MaxConn AttemptFails NewColumn\n\
                    Tcp: 17 1 4 -1 9 0\n";
        assert_eq!(
            parse_tcp(snmp),
            Ok(TcpCounters {
                curr_estab: 17,
                attempt_fails: 9,
                estab_resets: 4
            })
        );
    }

    #[test]
    fn a_broken_snmp_file_is_an_error_not_a_zero() {
        for broken in [
            "",
            "Tcp: CurrEstab AttemptFails EstabResets\n",
            "Tcp: CurrEstab AttemptFails EstabResets\nTcp: 1 2\n",
            "Tcp: CurrEstab AttemptFails\nTcp: 1 2\n",
            "Tcp: CurrEstab AttemptFails EstabResets\nTcp: 1 x 3\n",
        ] {
            assert!(parse_tcp(broken).is_err(), "{broken:?}");
        }
    }

    #[test]
    fn deltas_follow_the_windows_rule() {
        assert_eq!(
            counter_delta(None, 10),
            None,
            "the first read is the baseline"
        );
        assert_eq!(
            counter_delta(Some(10), 10),
            None,
            "a quiet minute posts nothing"
        );
        assert_eq!(counter_delta(Some(10), 13), Some(3));
        assert_eq!(counter_delta(Some(10), 4), None, "a counter that went down");
        assert_eq!(counter_delta(Some(0), u64::MAX), Some(i32::MAX));
    }

    #[test]
    fn dev_rows_are_split_at_the_colon() {
        let wsl = parse_dev(WSL_DEV).expect("parse");
        assert_eq!(
            wsl.keys().map(String::as_str).collect::<Vec<_>>(),
            ["docker0", "eth0", "lo"]
        );
        let host = parse_dev(WIFI_DOCKER_DEV).expect("parse");
        assert_eq!(
            host["wlp3s0"],
            DevCounters {
                rx_bytes: 18_446_744_073,
                tx_bytes: 2_345_678_901,
                errors: 0,
                drops: 1237,
            }
        );
        assert_eq!(
            host["br-5f1c2a9e0b7d"],
            DevCounters {
                rx_bytes: 22_334_455,
                tx_bytes: 33_445_566,
                ..DevCounters::default()
            }
        );
        assert_eq!(host.len(), 6);
    }

    #[test]
    fn a_broken_dev_file_is_an_error() {
        let header = "Inter-|   Receive\n face |bytes\n";
        for broken in [
            String::new(),
            "eth0: 1 2 3\n".to_string(),
            format!("{header}eth0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16\n"),
            format!("{header}eth0: 1 2 3\n"),
            format!("{header}eth0: 1 2 3 4 5 6 7 8 x 10 11 12 13 14 15 16\n"),
        ] {
            assert!(parse_dev(&broken).is_err(), "{broken:?}");
        }
    }

    #[test]
    fn only_device_backed_interfaces_are_physical() {
        let tree = FakeTree::new("net-physical");
        wifi_docker_sysfs(&tree);
        let sys = tree.0.join("sys");
        let interfaces = parse_dev(WIFI_DOCKER_DEV).unwrap();
        let physical: Vec<&str> = interfaces
            .keys()
            .map(String::as_str)
            .filter(|name| is_physical(&sys, name))
            .collect();
        assert_eq!(physical, ["enp2s0", "wlp3s0"]);
        assert!(is_loopback(&sys, "lo"));
        assert!(!is_loopback(&sys, "wlp3s0"));
        // Without sysfs, the name decides.
        assert!(is_loopback(&tree.0.join("nowhere"), "lo"));
    }

    #[test]
    fn speeds_follow_the_windows_sampler() {
        let octets = |rx: u64, tx: u64| DevCounters {
            rx_bytes: rx,
            tx_bytes: tx,
            ..DevCounters::default()
        };
        let mb = 1024 * 1024;
        let previous = BTreeMap::from([
            ("wlp3s0".to_string(), octets(0, 0)),
            ("enp2s0".to_string(), octets(5 * mb, 5 * mb)),
            ("usb0".to_string(), octets(9 * mb, 9 * mb)),
        ]);
        let current = BTreeMap::from([
            ("wlp3s0".to_string(), octets(20 * mb, 10 * mb)),
            ("enp2s0".to_string(), octets(5 * mb, 5 * mb)), // idle: no speed
            ("usb0".to_string(), octets(mb, 10 * mb)),      // rx went down: skipped
            ("wlx00c0ca".to_string(), octets(mb, mb)),      // first sighting: next time
        ]);
        let result = speeds(&previous, &current, Duration::from_secs(10));
        assert_eq!(
            result,
            BTreeMap::from([(
                "wlp3s0".to_string(),
                Speed {
                    rx_mb_per_sec: 2.0,
                    tx_mb_per_sec: 1.0
                }
            )])
        );
        assert!(speeds(&previous, &current, Duration::ZERO).is_empty());
    }

    #[test]
    fn the_link_follows_operstate_and_carrier_when_unknown() {
        let tree = FakeTree::new("net-link");
        wifi_docker_sysfs(&tree);
        let sys = tree.0.join("sys");
        assert_eq!(link_up(&sys, "wlp3s0"), Ok(true));
        assert_eq!(link_up(&sys, "enp2s0"), Ok(false));
        for (state, carrier, expected) in [
            ("dormant", None, Ok(false)),
            ("lowerlayerdown", None, Ok(false)),
            ("unknown", Some("1"), Ok(true)),
            ("unknown", Some("0"), Ok(false)),
        ] {
            tree.file("sys/class/net/tun0/operstate", &format!("{state}\n"));
            if let Some(carrier) = carrier {
                tree.file("sys/class/net/tun0/carrier", &format!("{carrier}\n"));
            }
            assert_eq!(link_up(&sys, "tun0"), expected, "{state} {carrier:?}");
        }
        tree.file("sys/class/net/tun0/operstate", "sideways\n");
        assert!(link_up(&sys, "tun0").is_err());
        assert!(link_up(&sys, "gone0").is_err(), "no such interface");

        assert_eq!(carrier_changes(&sys, "wlp3s0"), Some(2));
        assert_eq!(carrier_changes(&sys, "gone0"), None);
        assert!(is_wireless(&sys, "wlp3s0") && !is_wireless(&sys, "enp2s0"));
        assert_eq!(wireless_interfaces(&sys), ["wlp3s0"]);
        assert!(wireless_interfaces(&tree.0.join("nowhere")).is_empty());
    }

    #[test]
    fn wireless_rows_keep_only_updated_values_and_dbm_levels() {
        let rows = parse_wireless(WIFI_WIRELESS).expect("parse");
        assert_eq!(
            rows,
            BTreeMap::from([
                (
                    "wlp3s0".to_string(),
                    WirelessReading {
                        quality: Some(36.0),
                        signal_dbm: Some(-74.0)
                    }
                ),
                (
                    "wlx00c0ca".to_string(),
                    WirelessReading {
                        quality: Some(60.0),
                        signal_dbm: None
                    }
                ),
                ("wlan9".to_string(), WirelessReading::default()),
            ])
        );
        // A host with no associated Wi-Fi: the two header lines only.
        let headers: String = WIFI_WIRELESS
            .lines()
            .take(2)
            .map(|l| format!("{l}\n"))
            .collect();
        assert_eq!(parse_wireless(&headers), Ok(BTreeMap::new()));
        for broken in [
            String::new(),
            format!("{headers}wlp3s0 0000 36. -74.\n"),
            format!("{headers}wlp3s0: 0000 36.\n"),
            format!("{headers}wlp3s0: 0000 x. -74.\n"),
        ] {
            assert!(parse_wireless(&broken).is_err(), "{broken:?}");
        }
    }
}
