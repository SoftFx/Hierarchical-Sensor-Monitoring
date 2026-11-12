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

/// Received and transmitted octets of one interface (`/proc/net/dev` columns 1 and 9).
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct InterfaceOctets {
    pub rx_bytes: u64,
    pub tx_bytes: u64,
}

/// The 16 counter columns of a `/proc/net/dev` row (8 receive, 8 transmit).
const DEV_COLUMNS: usize = 16;
const TX_BYTES_COLUMN: usize = 8;

/// Parse `/proc/net/dev`: two header lines, then `<name>:<16 counters>` per interface. The kernel
/// pads the name to 6 characters and the first counter to 8, so a long name or a large counter
/// leaves no space after the `:` (`wlp3s0:18446744073 …`) — the row is split at the colon, which
/// an interface name cannot contain.
pub fn parse_dev(dev: &str) -> Result<BTreeMap<String, InterfaceOctets>, String> {
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
            InterfaceOctets {
                rx_bytes: column(0)?,
                tx_bytes: column(TX_BYTES_COLUMN)?,
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
    previous: &BTreeMap<String, InterfaceOctets>,
    current: &BTreeMap<String, InterfaceOctets>,
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

    /// `/sys/class/net` of [`WIFI_DOCKER_DEV`]: only the two NICs have a `device` link.
    pub fn wifi_docker_sysfs(tree: &FakeTree) {
        for (name, kind, physical) in [
            ("lo", "772", false),
            ("wlp3s0", "1", true),
            ("enp2s0", "1", true),
            ("docker0", "1", false),
            ("br-5f1c2a9e0b7d", "1", false),
            ("veth1a2b3c4", "1", false),
        ] {
            tree.file(&format!("sys/class/net/{name}/type"), &format!("{kind}\n"));
            if physical {
                tree.file(&format!("sys/class/net/{name}/device/uevent"), "");
            }
        }
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
            InterfaceOctets {
                rx_bytes: 18_446_744_073,
                tx_bytes: 2_345_678_901
            }
        );
        assert_eq!(
            host["br-5f1c2a9e0b7d"],
            InterfaceOctets {
                rx_bytes: 22_334_455,
                tx_bytes: 33_445_566
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
        let octets = |rx: u64, tx: u64| InterfaceOctets {
            rx_bytes: rx,
            tx_bytes: tx,
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
}
