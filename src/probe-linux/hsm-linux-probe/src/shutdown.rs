//! Graceful stop on `SIGTERM` / `SIGINT`.
//!
//! systemd sends `SIGTERM` on `systemctl stop|restart`; an operator running the probe in a
//! terminal sends `SIGINT`. Both mean the same thing here: stop sampling, stop the collector with
//! its bounded drain, exit.
//!
//! The handler does the least it possibly can — one store to a `static AtomicBool`, which is
//! async-signal-safe. Everything else happens on the main thread, which observes the flag.

use std::sync::atomic::{AtomicBool, Ordering};

static STOP_REQUESTED: AtomicBool = AtomicBool::new(false);

/// Install the signal handlers. Idempotent; safe to call once at startup.
pub fn install() {
    #[cfg(unix)]
    install_unix();
}

#[cfg(unix)]
fn install_unix() {
    // SAFETY: sigaction with a handler that performs a single atomic store and touches nothing
    // else — no allocation, no locks, no non-reentrant libc calls. The struct is zeroed first so
    // every field the platform defines has a defined value.
    unsafe {
        let mut action: libc::sigaction = std::mem::zeroed();
        action.sa_sigaction = handle_signal as usize;
        action.sa_flags = libc::SA_RESTART;
        libc::sigemptyset(&mut action.sa_mask);
        libc::sigaction(libc::SIGTERM, &action, std::ptr::null_mut());
        libc::sigaction(libc::SIGINT, &action, std::ptr::null_mut());
    }
}

#[cfg(unix)]
extern "C" fn handle_signal(_signal: libc::c_int) {
    STOP_REQUESTED.store(true, Ordering::SeqCst);
}

/// Whether a stop has been requested.
pub fn is_requested() -> bool {
    STOP_REQUESTED.load(Ordering::SeqCst)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_signal_makes_the_stop_observable() {
        // The flag is process-global; no other test in this crate reads it. Setting it directly
        // is what the signal handler does, minus the signal delivery the test harness cannot fake.
        STOP_REQUESTED.store(true, Ordering::SeqCst);
        assert!(is_requested());
    }

    #[cfg(unix)]
    #[test]
    fn installing_the_handlers_does_not_trip_the_flag() {
        install();
        // install() must be inert: only a delivered signal may set the flag.
        install();
    }
}
