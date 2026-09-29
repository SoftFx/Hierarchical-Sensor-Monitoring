//! The alerts the Docker sensors carry from registration (owner decisions, #1416).
//!
//! The only place that builds alerts for the Docker source; every threshold comes from
//! [`super::contract`]:
//!
//! | Sensor | Alert |
//! |---|---|
//! | `CPU` | bar mean > 90 % for 30 min → warning notification, repeated hourly |
//! | `Memory used %` | bar mean > 90 % → warning notification, repeated hourly |
//! | `Service status` | value ≠ Running for 5 min → notification, repeated hourly — **the Windows `ServiceStatusPrototype` alert byte for byte** |
//! | `Health` | value = unhealthy for 5 min → notification, repeated hourly |
//! | `Restart count` | value changed → notification |
//! | `OOM killed` | value = true → sensor Error + notification, repeated hourly |
//!
//! HSM alerts cannot raise a sensor to Warning (the server offers "set Error" only), so "Warning"
//! is the warning icon on the notification, exactly like the managed Total CPU / Free RAM alerts.
//! Scheduled notifications use the managed `ThenSendInstantHourlyScheduledNotification` shape
//! (instant send, then hourly while the condition holds).

use hsm_collector::{
    instant_hourly_schedule_anchor, Alert, AlertCombination, AlertDestination, AlertIcon,
    AlertKind, AlertOperation, AlertProperty, AlertRepeat, AlertTarget, BoolSensor, Collector,
    DoubleBarSensor, EnumSensor, IntSensor, Result,
};

use super::contract::{self, health, service_status};

/// A freshly registered Docker sensor that carries an alert.
pub enum Target<'a, 'c> {
    Cpu(&'a DoubleBarSensor<'c>),
    MemoryUsed(&'a DoubleBarSensor<'c>),
    ServiceStatus(&'a EnumSensor<'c>),
    Health(&'a EnumSensor<'c>),
    RestartCount(&'a IntSensor<'c>),
    OomKilled(&'a BoolSensor<'c>),
}

/// Attach the contract alert to a newly registered sensor. Before Start it rides the Start
/// registration; for a sensor created while the collector runs (a service seen after start) the
/// collector (0.10.0) posts the registration at its next cycle with the alert included.
pub fn attach(collector: &Collector, target: Target<'_, '_>) -> Result<()> {
    match target {
        Target::Cpu(sensor) => sensor.attach_alert(&cpu(collector)?),
        Target::MemoryUsed(sensor) => sensor.attach_alert(&memory_used(collector)?),
        Target::ServiceStatus(sensor) => sensor.attach_alert(&service_status(collector)?),
        Target::Health(sensor) => sensor.attach_alert(&health_alert(collector)?),
        Target::RestartCount(sensor) => sensor.attach_alert(&restart_count(collector)?),
        Target::OomKilled(sensor) => sensor.attach_alert(&oom_killed(collector)?),
    }
}

fn percent(value: f64) -> AlertTarget {
    AlertTarget::Const(format!("{value}"))
}

/// The managed `ThenSendInstantHourlyScheduledNotification(template)`: sent at once, then hourly
/// while the condition holds.
fn scheduled<'c>(
    builder: hsm_collector::AlertBuilder<'c>,
    template: &str,
) -> Result<hsm_collector::AlertBuilder<'c>> {
    builder.scheduled_notification(
        template,
        instant_hourly_schedule_anchor(),
        AlertRepeat::Hourly,
        true,
        AlertDestination::FromParent,
    )
}

/// CPU: bar mean above the threshold for the confirmation period.
fn cpu(collector: &Collector) -> Result<Alert<'_>> {
    let builder = collector.alert(AlertKind::Bar)?.condition(
        AlertCombination::And,
        AlertProperty::Mean,
        AlertOperation::GreaterThan,
        percent(contract::CPU_ALERT_MEAN_PERCENT),
    )?;
    Ok(
        scheduled(builder, "[$product]$path $property $operation $target %")?
            .confirmation_period(contract::CPU_ALERT_CONFIRMATION)?
            .icon(AlertIcon::Warning)?
            .build(),
    )
}

/// Memory used %: bar mean above the threshold.
fn memory_used(collector: &Collector) -> Result<Alert<'_>> {
    let builder = collector.alert(AlertKind::Bar)?.condition(
        AlertCombination::And,
        AlertProperty::Mean,
        AlertOperation::GreaterThan,
        percent(contract::MEMORY_ALERT_MEAN_PERCENT),
    )?;
    Ok(
        scheduled(builder, "[$product]$path $property $operation $target %")?
            .icon(AlertIcon::Warning)?
            .build(),
    )
}

/// Service status: the managed `ServiceStatusPrototype` alert — `IfValue(NotEqual, Running)
/// .AndConfirmationPeriod(5 min).ThenSendInstantHourlyScheduledNotification(...)` — so one HSM view
/// and template cover Windows services and Compose services alike.
fn service_status(collector: &Collector) -> Result<Alert<'_>> {
    let builder = collector.alert(AlertKind::Instant)?.condition(
        AlertCombination::And,
        AlertProperty::Value,
        AlertOperation::NotEqual,
        AlertTarget::Const(service_status::RUNNING.to_string()),
    )?;
    Ok(scheduled(builder, "[$product]$path $operation Running")?
        .confirmation_period(contract::SERVICE_STATUS_ALERT_CONFIRMATION)?
        .build())
}

/// Health: unhealthy for the confirmation period.
fn health_alert(collector: &Collector) -> Result<Alert<'_>> {
    let builder = collector.alert(AlertKind::Instant)?.condition(
        AlertCombination::And,
        AlertProperty::Value,
        AlertOperation::Equal,
        AlertTarget::Const(health::UNHEALTHY.to_string()),
    )?;
    Ok(scheduled(builder, "[$product]$path is unhealthy")?
        .confirmation_period(contract::HEALTH_ALERT_CONFIRMATION)?
        .build())
}

/// Restart count: the value changed. The sensor posts only on change, so this fires once per
/// restart burst; `IsChanged` (not `ReceivedNewValue`) so the first baseline post of a newly seen
/// service does not notify.
fn restart_count(collector: &Collector) -> Result<Alert<'_>> {
    Ok(collector
        .alert(AlertKind::Instant)?
        .condition(
            AlertCombination::And,
            AlertProperty::Value,
            AlertOperation::IsChanged,
            AlertTarget::LastValue,
        )?
        .notification(
            "[$product]$path restarted ($property $operation)",
            AlertDestination::FromParent,
        )?
        .icon(AlertIcon::Warning)?
        .build())
}

/// OOM killed: true → the sensor goes to Error, with a notification.
fn oom_killed(collector: &Collector) -> Result<Alert<'_>> {
    let builder = collector.alert(AlertKind::Instant)?.condition(
        AlertCombination::And,
        AlertProperty::Value,
        AlertOperation::Equal,
        AlertTarget::Const("True".to_string()),
    )?;
    Ok(
        scheduled(builder, "[$product]$path: killed for running out of memory")?
            .icon(AlertIcon::Error)?
            .sensor_error()?
            .build(),
    )
}
