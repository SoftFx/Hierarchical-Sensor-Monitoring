//! Clocks shared by the helper and the probe.

/// `(local day number, second of that day)` for a Unix time in milliseconds, in the host's
/// timezone. Injectable so tests do not depend on the machine's zone.
pub type LocalTime = fn(i64) -> (i64, i64);

/// [`LocalTime`] of the host: `localtime_r`'s UTC offset (so DST is honored), UTC if unknown.
pub fn local_time(unix_ms: i64) -> (i64, i64) {
    let seconds = unix_ms.div_euclid(1000);
    let local = seconds + utc_offset(seconds).unwrap_or(0);
    (local.div_euclid(86_400), local.rem_euclid(86_400))
}

#[cfg(unix)]
fn utc_offset(seconds: i64) -> Option<i64> {
    // glibc's localtime_r reads the zone once per process; tzset re-reads TZ and /etc/localtime
    // (a stat when unchanged), so a `timedatectl set-timezone` applies without a restart.
    extern "C" {
        // POSIX <time.h>; not bound by the libc crate.
        fn tzset();
    }
    // SAFETY: tzset takes no arguments and only updates libc's own zone state.
    unsafe { tzset() };
    let time = libc::time_t::try_from(seconds).ok()?;
    // SAFETY: `tm` is plain data; localtime_r only writes into it.
    let mut tm: libc::tm = unsafe { std::mem::zeroed() };
    // SAFETY: both pointers are valid for the duration of the call.
    let result = unsafe { libc::localtime_r(&time, &mut tm) };
    if result.is_null() {
        return None;
    }
    // The field type differs between libc targets, hence `as`.
    #[allow(clippy::unnecessary_cast)]
    Some(tm.tm_gmtoff as i64)
}

#[cfg(not(unix))]
fn utc_offset(_seconds: i64) -> Option<i64> {
    None
}

/// `CLOCK_MONOTONIC` in nanoseconds: one clock for every process of a boot, so the probe can tell
/// how old the helper's snapshot is, whatever the wall clock does.
#[cfg(unix)]
pub fn monotonic_ns() -> u64 {
    // SAFETY: `ts` is plain data that clock_gettime fills.
    let mut ts: libc::timespec = unsafe { std::mem::zeroed() };
    // SAFETY: a valid clock id and a valid pointer; CLOCK_MONOTONIC cannot fail on Linux.
    unsafe { libc::clock_gettime(libc::CLOCK_MONOTONIC, &mut ts) };
    #[allow(clippy::unnecessary_cast)]
    let (secs, nanos) = (ts.tv_sec as u64, ts.tv_nsec as u64);
    secs.saturating_mul(1_000_000_000).saturating_add(nanos)
}

#[cfg(not(unix))]
pub fn monotonic_ns() -> u64 {
    0
}

/// The wall clock in Unix milliseconds.
pub fn unix_now_ms() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|elapsed| i64::try_from(elapsed.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}
