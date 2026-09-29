//! Safe RAII wrapper over the HSM native collector's stable C ABI.
//!
//! Scope: the subset the Linux probe needs — options, HTTP transport, lifecycle, a log sink,
//! instant / enum / double-bar sensors, and alerts attached at registration. Everything else stays
//! behind [`hsm_collector_sys`].
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

mod alert;
mod collector;
mod error;
mod options;
mod sensor;

pub use alert::{
    instant_hourly_schedule_anchor, Alert, AlertBuilder, AlertCombination, AlertDestination,
    AlertIcon, AlertKind, AlertOperation, AlertProperty, AlertRepeat, AlertTarget,
};
pub use collector::{Collector, DefaultSensor, LINUX_METRIC_SOURCES_AVAILABLE};
pub use error::{Error, Result};
pub use options::{
    CollectorOptions, CollectorStatus, EnumOption, LogLevel, SensorOptions, SensorStatus,
    STATISTICS_EMA,
};
pub use sensor::{
    BoolSensor, DoubleBarSensor, DoubleSensor, EnumSensor, IntSensor, StringSensor, VersionSensor,
};

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
    use std::time::Duration;

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

    /// A collector with a fixed computer name, so registration paths are deterministic.
    fn named_collector() -> Collector {
        let mut options = test_options();
        options.computer_name = Some("unit-host".into());
        Collector::new(&options).expect("create")
    }

    /// The single registration recorded at Start, as the canonical text the conformance corpus
    /// asserts on (`expect_registration_contains`).
    fn only_registration(collector: &Collector) -> String {
        collector.start().expect("start");
        let registrations = collector.registrations();
        collector.stop().expect("stop");
        assert_eq!(registrations.len(), 1, "{registrations:#?}");
        registrations.into_iter().next().unwrap()
    }

    #[test]
    fn a_bar_alert_registers_in_the_managed_alert_shape() {
        // The shape the probe's CPU temperature sensor uses. Compare with
        // tests/conformance/collector/alert_registration_contract.hsmtest, which pins the same
        // AlertUpdateRequest serialization across both collectors.
        let collector = named_collector();
        let bar = collector
            .double_bar_sensor(
                ".computer/bar",
                Duration::from_secs(300),
                Duration::from_secs(15),
                2,
                &SensorOptions::default().with_is_computer_sensor(true),
            )
            .expect("bar");
        let alert = collector
            .alert(AlertKind::Bar)
            .and_then(|alert| {
                alert.condition(
                    AlertCombination::And,
                    AlertProperty::Mean,
                    AlertOperation::GreaterThan,
                    AlertTarget::Const("80".into()),
                )
            })
            .and_then(|alert| {
                alert.condition(
                    AlertCombination::And,
                    AlertProperty::Mean,
                    AlertOperation::LessThanOrEqual,
                    AlertTarget::Const("90".into()),
                )
            })
            .and_then(|alert| {
                alert.scheduled_notification(
                    "hot",
                    instant_hourly_schedule_anchor(),
                    AlertRepeat::Hourly,
                    true,
                    AlertDestination::FromParent,
                )
            })
            .and_then(|alert| alert.icon(AlertIcon::Warning))
            .expect("alert")
            .build();
        bar.attach_alert(&alert).expect("attach");

        let json = only_registration(&collector);
        assert!(
            json.contains("\"Path\":\"unit-host/.computer/bar\""),
            "{json}"
        );
        assert!(
            json.contains(
                "\"Alerts\":[{\"Conditions\":[\
                 {\"Combination\":0,\"Operation\":2,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"80\"}},\
                 {\"Combination\":0,\"Operation\":0,\"Property\":103,\"Target\":{\"Type\":0,\"Value\":\"90\"}}],\
                 \"Status\":1,\"DestinationMode\":3,\"Template\":\"hot\",\"Icon\":\"\\u26A0\",\
                 \"IsDisabled\":false,\"ConfirmationPeriod\":null,\
                 \"ScheduledNotificationTime\":\"0001-01-01T12:00:00Z\",\"ScheduledRepeatMode\":20,\
                 \"ScheduledInstantSend\":true}]"
            ),
            "{json}"
        );
        assert!(json.contains("\"TtlAlerts\":null"), "{json}");
    }

    #[test]
    fn an_error_alert_raises_the_status_and_a_ttl_alert_lands_in_ttl_alerts() {
        let collector = named_collector();
        let sensor = collector
            .double_sensor("probe/free", &SensorOptions::default())
            .expect("double");
        let error = collector
            .alert(AlertKind::Instant)
            .and_then(|alert| {
                alert.condition(
                    AlertCombination::And,
                    AlertProperty::Value,
                    AlertOperation::LessThan,
                    AlertTarget::Const("5".into()),
                )
            })
            .and_then(|alert| alert.notification("low", AlertDestination::AllChats))
            .and_then(|alert| alert.icon_raw("X"))
            .and_then(|alert| alert.sensor_error())
            .and_then(|alert| alert.confirmation_period(Duration::from_secs(300)))
            .and_then(|alert| alert.disabled(true))
            .expect("alert")
            .build();
        let inactive = collector
            .alert(AlertKind::Ttl)
            .and_then(|alert| alert.inactivity_period(Duration::from_secs(60)))
            .and_then(|alert| alert.notification("gone", AlertDestination::FromParent))
            .expect("ttl alert")
            .build();
        sensor.attach_alert(&error).expect("attach error alert");
        sensor.attach_alert(&inactive).expect("attach ttl alert");

        let json = only_registration(&collector);
        assert!(
            json.contains(
                "\"Alerts\":[{\"Conditions\":[{\"Combination\":0,\"Operation\":1,\"Property\":20,\
                 \"Target\":{\"Type\":0,\"Value\":\"5\"}}],\"Status\":3,\"DestinationMode\":200,\
                 \"Template\":\"low\",\"Icon\":\"X\",\"IsDisabled\":true,\
                 \"ConfirmationPeriod\":3000000000,"
            ),
            "{json}"
        );
        assert!(
            json.contains(
                "\"TtlAlerts\":[{\"Conditions\":[],\"Status\":1,\"DestinationMode\":3,\
                 \"Template\":\"gone\""
            ),
            "{json}"
        );
        assert!(json.contains("\"TTLTicks\":[600000000]"), "{json}");
    }

    #[test]
    fn an_enum_sensor_with_options_registers_the_service_status_shape() {
        // Mirrors the managed ServiceStatusPrototype (and the conformance case
        // options_surface_contract:enum_full_options_with_state_alert): EnumOptions +
        // AggregateData + "not Running for 5 minutes".
        let collector = named_collector();
        let running = EnumOption {
            key: 4,
            value: "Running".into(),
            color: 0x00FF00,
            description: Some("The service is running.".into()),
        };
        let stopped = EnumOption {
            key: 1,
            value: "Stopped".into(),
            color: 0xFF0000,
            description: Some("The service is stopped.".into()),
        };
        let state = collector
            .enum_sensor_with_options(
                "Docker/app/web/Service status",
                &SensorOptions::default()
                    .with_description("state")
                    .with_aggregate_data(true)
                    .with_enable_grafana(false)
                    .with_ttl(Duration::from_secs(60)),
                &[stopped, running],
            )
            .expect("enum");
        let alert = collector
            .alert(AlertKind::Instant)
            .and_then(|alert| {
                alert.condition(
                    AlertCombination::And,
                    AlertProperty::Value,
                    AlertOperation::NotEqual,
                    AlertTarget::Const("4".into()),
                )
            })
            .and_then(|alert| alert.confirmation_period(Duration::from_secs(300)))
            .and_then(|alert| {
                alert.scheduled_notification(
                    "down",
                    instant_hourly_schedule_anchor(),
                    AlertRepeat::Hourly,
                    true,
                    AlertDestination::FromParent,
                )
            })
            .expect("alert")
            .build();
        state.attach_alert(&alert).expect("attach");

        let json = only_registration(&collector);
        for expected in [
            "\"Path\":\"unit-host/UnitTest/Docker/app/web/Service status\"",
            "\"SensorType\":10,",
            "\"TTLTicks\":[600000000]",
            "\"Description\":\"state\"",
            "\"EnumOptions\":[{\"Key\":1,\"Value\":\"Stopped\",\"Color\":16711680,\
             \"Description\":\"The service is stopped.\"},{\"Key\":4,\"Value\":\"Running\",\
             \"Color\":65280,\"Description\":\"The service is running.\"}]",
            "\"DisplayUnit\":null",
            "\"AggregateData\":true",
            "\"EnableGrafana\":false",
            "\"Operation\":5,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"4\"}",
            "\"ConfirmationPeriod\":3000000000",
        ] {
            assert!(json.contains(expected), "missing {expected} in {json}");
        }
    }

    #[test]
    fn computer_anchoring_and_null_tri_states_by_default() {
        let collector = named_collector();
        collector
            .int_sensor(
                ".computer/Logical cores",
                &SensorOptions::default().with_is_computer_sensor(true),
            )
            .expect("int");
        let json = only_registration(&collector);
        assert!(
            json.contains("\"Path\":\"unit-host/.computer/Logical cores\""),
            "{json}"
        );
        assert!(json.contains("\"IsSingletonSensor\":true"), "{json}");
        assert!(json.contains("\"AggregateData\":null"), "{json}");
        assert!(json.contains("\"EnableGrafana\":null"), "{json}");
        assert!(json.contains("\"Alerts\":null"), "{json}");
    }

    #[test]
    fn a_description_changed_while_running_replaces_the_registration() {
        let collector = named_collector();
        let sensor = collector
            .int_sensor(
                "probe/described",
                &SensorOptions::default().with_description("limit 1024 MB"),
            )
            .expect("int");
        collector.start().expect("start");
        sensor
            .set_description(Some("limit 2048 MB"))
            .expect("set while running");
        let registrations = collector.registrations();
        assert_eq!(registrations.len(), 1, "replaced in place");
        assert!(
            registrations[0].contains("limit 2048 MB"),
            "{registrations:?}"
        );
        assert!(sensor.set_description(Some("bad\0text")).is_err());
        collector.stop().expect("stop");
    }

    #[test]
    fn an_alert_attached_while_running_reaches_the_registration() {
        // Collector 0.9.1: a sensor created while the collector runs (a service the probe sees
        // after start) takes its alerts right after creation, and its registration for the run
        // is re-recorded with them (the live transport re-posts it).
        let collector = named_collector();
        collector.start().expect("start");
        let sensor = collector
            .int_sensor("probe/late", &SensorOptions::default())
            .expect("int");
        let alert = collector
            .alert(AlertKind::Instant)
            .and_then(|alert| alert.notification("late-alert", AlertDestination::FromParent))
            .expect("alert")
            .build();
        sensor.attach_alert(&alert).expect("attach while running");
        let registrations = collector.registrations();
        assert_eq!(registrations.len(), 1, "one registration for the run");
        assert!(registrations[0].contains("late-alert"), "{registrations:?}");
        collector.stop().expect("stop");
        // Stopped: the next Start re-registers, so attaching is effective and allowed too.
        sensor.attach_alert(&alert).expect("attach while stopped");
    }

    #[test]
    fn an_alert_from_another_collector_is_refused() {
        let first = named_collector();
        let second = named_collector();
        let sensor = first
            .int_sensor("probe/x", &SensorOptions::default())
            .expect("int");
        let foreign = second.alert(AlertKind::Instant).expect("alert").build();
        let error = sensor.attach_alert(&foreign).expect_err("foreign alert");
        assert_eq!(
            error.code(),
            Some(hsm_collector_sys::HSM_RESULT_INVALID_ARGUMENT)
        );
    }

    #[test]
    fn interior_nul_in_an_alert_template_is_rejected() {
        let collector = named_collector();
        let error = collector
            .alert(AlertKind::Instant)
            .and_then(|alert| alert.notification("bad\0template", AlertDestination::FromParent))
            .expect_err("a NUL byte must be rejected");
        assert!(matches!(error, Error::InteriorNul { .. }));
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
