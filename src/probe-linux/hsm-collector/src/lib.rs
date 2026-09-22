//! Safe RAII wrapper over the HSM native collector's stable C ABI.
//!
//! Scope: the subset the Linux probe needs — options, HTTP transport, lifecycle, a log sink, and
//! instant / enum / double-bar sensors. Everything else stays behind [`hsm_collector_sys`].
//!
//! # FFI rules this crate enforces
//!
//! * **No unwinding into C.** Every `extern "C"` callback body is wrapped in `catch_unwind`; a
//!   panicking Rust callback drops its message instead of crossing the boundary.
//! * **Handles cannot outlive their collector.** Sensor handles borrow the [`Collector`], so the
//!   compiler rejects the use-after-free the raw ABI would allow.
//! * **Thread-safety markers follow the header, not convenience.** `Send`/`Sync` are asserted only
//!   where `hsm_collector.h` documents the entry points as callable from any thread; the calls it
//!   asks the caller to serialize are serialized by an internal lock.
//! * **Secrets stay out of diagnostics.** The access key is redacted from `Debug`, and error text
//!   never echoes caller-supplied strings.

mod collector;
mod error;
mod options;
mod sensor;

pub use collector::{Collector, LINUX_METRIC_SOURCES_AVAILABLE};
pub use error::{Error, Result};
pub use options::{
    CollectorOptions, CollectorStatus, EnumOption, LogLevel, SensorOptions, SensorStatus,
};
pub use sensor::{BoolSensor, DoubleBarSensor, DoubleSensor, EnumSensor, IntSensor, StringSensor};

/// The linked collector library's packed version (`MAJOR * 10000 + MINOR * 100 + PATCH`).
pub fn library_version() -> i32 {
    // SAFETY: a pure accessor with no arguments and no state.
    unsafe { hsm_collector_sys::hsm_collector_version() }
}

/// `MAJOR.MINOR.PATCH` of the linked collector library.
pub fn library_version_string() -> String {
    let packed = library_version();
    format!(
        "{}.{}.{}",
        packed / 10000,
        (packed / 100) % 100,
        packed % 100
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Mutex};

    fn test_options() -> CollectorOptions {
        // Port 1 with no transport installed: nothing is ever sent, so no listener is needed.
        let mut options = CollectorOptions::new("unit-test-key", "http://127.0.0.1", 1);
        options.allow_plaintext_transport = true;
        options.module = Some("UnitTest".into());
        options
    }

    #[test]
    fn library_version_is_readable() {
        assert!(library_version() > 0);
        assert_eq!(library_version_string().split('.').count(), 3);
    }

    #[test]
    fn debug_output_never_contains_the_access_key() {
        let options = CollectorOptions::new("super-secret-key", "https://example.invalid", 44330);
        let rendered = format!("{options:?}");
        assert!(
            !rendered.contains("super-secret-key"),
            "Debug leaked the access key: {rendered}"
        );
        assert!(rendered.contains("<redacted>"));
    }

    #[test]
    fn lifecycle_runs_through_the_documented_states() {
        let collector = Collector::new(&test_options()).expect("create");
        assert_eq!(collector.status(), CollectorStatus::Stopped);
        collector.start().expect("start");
        assert_eq!(collector.status(), CollectorStatus::Running);
        // Start and Stop are documented as idempotent.
        collector.start().expect("second start is a no-op");
        collector.stop().expect("stop");
        collector.stop().expect("second stop is a no-op");
        assert_eq!(collector.status(), CollectorStatus::Stopped);
        collector.dispose();
        assert_eq!(collector.status(), CollectorStatus::Disposed);
    }

    #[test]
    fn a_panicking_log_sink_is_contained() {
        let collector = Collector::new(&test_options()).expect("create");
        collector
            .set_logger(|_level, _message| panic!("a broken sink must not reach C++"))
            .expect("set logger");
        // The collector logs during start/stop; if the panic escaped the trampoline the process
        // would abort instead of reaching the assertion below.
        collector.start().expect("start");
        collector.stop().expect("stop");
        assert_eq!(collector.status(), CollectorStatus::Stopped);
    }

    #[test]
    fn log_sink_receives_collector_diagnostics() {
        let captured = Arc::new(Mutex::new(Vec::new()));
        let sink = Arc::clone(&captured);
        let collector = Collector::new(&test_options()).expect("create");
        collector
            .set_logger(move |level, message| {
                sink.lock()
                    .unwrap()
                    .push(format!("{}|{message}", level.as_str()));
            })
            .expect("set logger");

        collector.start().expect("start");
        collector.stop().expect("stop");

        let lines = captured.lock().unwrap();
        assert!(
            !lines.is_empty(),
            "the collector must emit at least one lifecycle message"
        );
        assert!(
            lines.iter().all(|line| !line.contains("unit-test-key")),
            "collector diagnostics must never echo the access key: {lines:?}"
        );
    }

    #[test]
    fn sensors_of_every_bound_kind_accept_values() {
        let collector = Collector::new(&test_options()).expect("create");
        let options = SensorOptions::default().with_ttl(std::time::Duration::from_secs(180));

        let double = collector
            .double_sensor("probe/double", &options)
            .expect("double");
        let int = collector
            .int_sensor("probe/int", &SensorOptions::default())
            .expect("int");
        let flag = collector
            .bool_sensor("probe/bool", &SensorOptions::default())
            .expect("bool");
        let text = collector
            .string_sensor("probe/string", &SensorOptions::default())
            .expect("string");
        let state = collector
            .enum_sensor(
                "probe/enum",
                Some("source status"),
                &[
                    EnumOption::new(0, "ok"),
                    EnumOption::new(1, "degraded"),
                    EnumOption::new(2, "failed"),
                ],
            )
            .expect("enum");
        let bar = collector
            .double_bar_sensor(
                "probe/bar",
                std::time::Duration::from_secs(300),
                std::time::Duration::from_secs(60),
                2,
                &SensorOptions::default(),
            )
            .expect("bar");

        collector.start().expect("start");
        double.add(1.5).expect("double value");
        int.add(7).expect("int value");
        flag.add_with(true, SensorStatus::Warning, Some("comment"))
            .expect("bool value");
        text.add("hello").expect("string value");
        state.add(1).expect("enum value");
        bar.add(0.25).expect("bar sample");
        collector.stop().expect("stop");
    }

    #[test]
    fn interior_nul_in_a_path_is_rejected_without_reaching_the_abi() {
        let collector = Collector::new(&test_options()).expect("create");
        let error = collector
            .double_sensor("probe/bad\0path", &SensorOptions::default())
            .expect_err("a NUL byte must be rejected");
        assert!(matches!(error, Error::InteriorNul { .. }));
    }

    #[test]
    fn linux_metric_sources_report_their_availability_honestly() {
        let collector = Collector::new(&test_options()).expect("create");
        let result = collector.install_linux_metric_sources();
        if LINUX_METRIC_SOURCES_AVAILABLE {
            // On a non-Linux host the ABI rejects it with INVALID_STATE, which is still an honest
            // answer; the point of the assertion is that it is never Unsupported.
            assert!(!matches!(result, Err(Error::Unsupported { .. })));
        } else {
            assert!(matches!(result, Err(Error::Unsupported { .. })));
        }
    }
}
