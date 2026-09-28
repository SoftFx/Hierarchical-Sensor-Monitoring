//! The alerts the Docker sensors carry from registration (owner decisions, #1416).
//!
//! This is the only place that needs the wrapper's alert API. Every threshold comes from
//! [`super::contract`]:
//!
//! | Sensor | Alert |
//! |---|---|
//! | `CPU` | bar mean > [`CPU_ALERT_MEAN_PERCENT`](super::contract::CPU_ALERT_MEAN_PERCENT) → Warning + notification, confirmation [`CPU_ALERT_CONFIRMATION`](super::contract::CPU_ALERT_CONFIRMATION) |
//! | `Memory used %` | bar mean > [`MEMORY_ALERT_MEAN_PERCENT`](super::contract::MEMORY_ALERT_MEAN_PERCENT) → Warning + notification |
//! | `Service status` | `IfValue NotEqual Running`, confirmation [`SERVICE_STATUS_ALERT_CONFIRMATION`](super::contract::SERVICE_STATUS_ALERT_CONFIRMATION) + notification — exactly the Windows `ServiceStatusPrototype` alert |
//! | `Health` | value == `unhealthy`, confirmation [`HEALTH_ALERT_CONFIRMATION`](super::contract::HEALTH_ALERT_CONFIRMATION) + notification |
//! | `Restart count` | new value (the sensor posts only on change) + notification |
//! | `OOM killed` | value == true → Error + notification |
//! | `Memory limit` | none (informational) |

use hsm_collector::{BoolSensor, Collector, DoubleBarSensor, EnumSensor, IntSensor};

/// A freshly registered Docker sensor that may carry an alert.
// TODO(#1416): the handles are read once `attach` is wired; drop this allow then.
#[allow(dead_code)]
pub enum AlertTarget<'a, 'c> {
    Cpu(&'a DoubleBarSensor<'c>),
    MemoryUsed(&'a DoubleBarSensor<'c>),
    ServiceStatus(&'a EnumSensor<'c>),
    Health(&'a EnumSensor<'c>),
    RestartCount(&'a IntSensor<'c>),
    OomKilled(&'a BoolSensor<'c>),
}

/// Attach the contract alert to a newly registered sensor.
///
/// TODO(#1416): wire when the wrapper alert API lands (`Collector::alert(AlertKind)` →
/// `AlertBuilder` → `attach_alert(&Alert)` on the sensor handle). Until then the sensors register
/// without alerts; the table in the module docs is what this function must attach.
pub fn attach(_collector: &Collector, target: AlertTarget<'_, '_>) -> hsm_collector::Result<()> {
    match target {
        AlertTarget::Cpu(_)
        | AlertTarget::MemoryUsed(_)
        | AlertTarget::ServiceStatus(_)
        | AlertTarget::Health(_)
        | AlertTarget::RestartCount(_)
        | AlertTarget::OomKilled(_) => Ok(()),
    }
}
