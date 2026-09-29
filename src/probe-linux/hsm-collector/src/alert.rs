//! Alert builders: the collector's alert DSL (`hsm_collector_create_alert` + `hsm_alert_*`), which
//! mirrors the managed `HSMDataCollector.Alerts` DSL and lands in the sensor's registration payload
//! (`AddOrUpdateSensorRequest.Alerts` / `TtlAlerts`).
//!
//! ```ignore
//! let alert = collector
//!     .alert(AlertKind::Bar)?
//!     .condition(AlertCombination::And, AlertProperty::Mean, AlertOperation::GreaterThan,
//!                AlertTarget::Const("90".into()))?
//!     .notification("[$product]$path $property $operation $target", AlertDestination::FromParent)?
//!     .icon(AlertIcon::Error)?
//!     .sensor_error()?
//!     .build();
//! sensor.attach_alert(&alert)?; // before Collector::start
//! ```
//!
//! Lifetime: an alert handle belongs to the collector and is freed with it (the ABI has no release),
//! so [`Alert`] only borrows the [`Collector`]. Attaching copies the alert into the sensor's
//! registration, so one alert may be attached to several sensors.

use std::ffi::CString;
use std::ptr;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use hsm_collector_sys as sys;

use crate::error::{Error, Result};
use crate::options::clamp_millis;
use crate::Collector;

/// Which registration list the alert lands in. `Instant` and `Bar` both go to `Alerts` (they only
/// differ in which condition properties make sense); `Ttl` goes to `TtlAlerts` and its inactivity
/// period also becomes the sensor's TTL.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertKind {
    Instant,
    Bar,
    Ttl,
}

/// How a condition joins the ones before it (the managed DSL always stamps `And`).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertCombination {
    And,
    Or,
}

/// The value a condition inspects (managed `AlertProperty`).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertProperty {
    Status,
    Comment,
    Value,
    Min,
    Max,
    Mean,
    Count,
    LastValue,
    FirstValue,
    Length,
    OriginalSize,
    NewSensorData,
    EmaValue,
    EmaMin,
    EmaMax,
    EmaMean,
    EmaCount,
}

/// The comparison a condition applies (managed `AlertOperation`).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertOperation {
    LessThanOrEqual,
    LessThan,
    GreaterThan,
    GreaterThanOrEqual,
    Equal,
    NotEqual,
    IsChanged,
    IsError,
    IsOk,
    IsChangedToError,
    IsChangedToOk,
    Contains,
    StartsWith,
    EndsWith,
    ReceivedNewValue,
}

/// What a condition compares against. `Const` carries the comparand as the text the managed DSL
/// would produce with `value.ToString()` (e.g. `"80"`, `"4"`).
#[derive(Clone, Debug, Eq, PartialEq)]
pub enum AlertTarget {
    Const(String),
    LastValue,
}

/// Where the alert's notification goes (managed `AlertDestinationMode`).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertDestination {
    NotInitialized,
    Empty,
    FromParent,
    AllChats,
}

/// Built-in icons; the collector maps each to the emoji the managed `AlertIcon.ToUtf8()` produces.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertIcon {
    Ok,
    Warning,
    Error,
    Pause,
    ArrowUp,
    ArrowDown,
    Clock,
    Hourglass,
}

/// Repeat cadence of a scheduled notification (managed `AlertRepeatMode`).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum AlertRepeat {
    FiveMinutes,
    TenMinutes,
    FifteenMinutes,
    ThirtyMinutes,
    Hourly,
    Daily,
    Weekly,
}

impl AlertKind {
    pub(crate) fn as_raw(self) -> sys::hsm_alert_kind_t {
        match self {
            AlertKind::Instant => sys::HSM_ALERT_KIND_INSTANT,
            AlertKind::Bar => sys::HSM_ALERT_KIND_BAR,
            AlertKind::Ttl => sys::HSM_ALERT_KIND_TTL,
        }
    }
}

impl AlertCombination {
    fn as_raw(self) -> sys::hsm_alert_combination_t {
        match self {
            AlertCombination::And => sys::HSM_ALERT_COMBINATION_AND,
            AlertCombination::Or => sys::HSM_ALERT_COMBINATION_OR,
        }
    }
}

impl AlertProperty {
    fn as_raw(self) -> sys::hsm_alert_property_t {
        match self {
            AlertProperty::Status => sys::HSM_ALERT_PROP_STATUS,
            AlertProperty::Comment => sys::HSM_ALERT_PROP_COMMENT,
            AlertProperty::Value => sys::HSM_ALERT_PROP_VALUE,
            AlertProperty::Min => sys::HSM_ALERT_PROP_MIN,
            AlertProperty::Max => sys::HSM_ALERT_PROP_MAX,
            AlertProperty::Mean => sys::HSM_ALERT_PROP_MEAN,
            AlertProperty::Count => sys::HSM_ALERT_PROP_COUNT,
            AlertProperty::LastValue => sys::HSM_ALERT_PROP_LAST_VALUE,
            AlertProperty::FirstValue => sys::HSM_ALERT_PROP_FIRST_VALUE,
            AlertProperty::Length => sys::HSM_ALERT_PROP_LENGTH,
            AlertProperty::OriginalSize => sys::HSM_ALERT_PROP_ORIGINAL_SIZE,
            AlertProperty::NewSensorData => sys::HSM_ALERT_PROP_NEW_SENSOR_DATA,
            AlertProperty::EmaValue => sys::HSM_ALERT_PROP_EMA_VALUE,
            AlertProperty::EmaMin => sys::HSM_ALERT_PROP_EMA_MIN,
            AlertProperty::EmaMax => sys::HSM_ALERT_PROP_EMA_MAX,
            AlertProperty::EmaMean => sys::HSM_ALERT_PROP_EMA_MEAN,
            AlertProperty::EmaCount => sys::HSM_ALERT_PROP_EMA_COUNT,
        }
    }
}

impl AlertOperation {
    fn as_raw(self) -> sys::hsm_alert_operation_t {
        match self {
            AlertOperation::LessThanOrEqual => sys::HSM_ALERT_OP_LESS_THAN_OR_EQUAL,
            AlertOperation::LessThan => sys::HSM_ALERT_OP_LESS_THAN,
            AlertOperation::GreaterThan => sys::HSM_ALERT_OP_GREATER_THAN,
            AlertOperation::GreaterThanOrEqual => sys::HSM_ALERT_OP_GREATER_THAN_OR_EQUAL,
            AlertOperation::Equal => sys::HSM_ALERT_OP_EQUAL,
            AlertOperation::NotEqual => sys::HSM_ALERT_OP_NOT_EQUAL,
            AlertOperation::IsChanged => sys::HSM_ALERT_OP_IS_CHANGED,
            AlertOperation::IsError => sys::HSM_ALERT_OP_IS_ERROR,
            AlertOperation::IsOk => sys::HSM_ALERT_OP_IS_OK,
            AlertOperation::IsChangedToError => sys::HSM_ALERT_OP_IS_CHANGED_TO_ERROR,
            AlertOperation::IsChangedToOk => sys::HSM_ALERT_OP_IS_CHANGED_TO_OK,
            AlertOperation::Contains => sys::HSM_ALERT_OP_CONTAINS,
            AlertOperation::StartsWith => sys::HSM_ALERT_OP_STARTS_WITH,
            AlertOperation::EndsWith => sys::HSM_ALERT_OP_ENDS_WITH,
            AlertOperation::ReceivedNewValue => sys::HSM_ALERT_OP_RECEIVED_NEW_VALUE,
        }
    }
}

impl AlertDestination {
    fn as_raw(self) -> sys::hsm_alert_destination_mode_t {
        match self {
            AlertDestination::NotInitialized => sys::HSM_ALERT_DESTINATION_NOT_INITIALIZED,
            AlertDestination::Empty => sys::HSM_ALERT_DESTINATION_EMPTY,
            AlertDestination::FromParent => sys::HSM_ALERT_DESTINATION_FROM_PARENT,
            AlertDestination::AllChats => sys::HSM_ALERT_DESTINATION_ALL_CHATS,
        }
    }
}

impl AlertIcon {
    fn as_raw(self) -> sys::hsm_alert_icon_t {
        match self {
            AlertIcon::Ok => sys::HSM_ALERT_ICON_OK,
            AlertIcon::Warning => sys::HSM_ALERT_ICON_WARNING,
            AlertIcon::Error => sys::HSM_ALERT_ICON_ERROR,
            AlertIcon::Pause => sys::HSM_ALERT_ICON_PAUSE,
            AlertIcon::ArrowUp => sys::HSM_ALERT_ICON_ARROW_UP,
            AlertIcon::ArrowDown => sys::HSM_ALERT_ICON_ARROW_DOWN,
            AlertIcon::Clock => sys::HSM_ALERT_ICON_CLOCK,
            AlertIcon::Hourglass => sys::HSM_ALERT_ICON_HOURGLASS,
        }
    }
}

impl AlertRepeat {
    fn as_raw(self) -> sys::hsm_alert_repeat_mode_t {
        match self {
            AlertRepeat::FiveMinutes => sys::HSM_ALERT_REPEAT_FIVE_MINUTES,
            AlertRepeat::TenMinutes => sys::HSM_ALERT_REPEAT_TEN_MINUTES,
            AlertRepeat::FifteenMinutes => sys::HSM_ALERT_REPEAT_FIFTEEN_MINUTES,
            AlertRepeat::ThirtyMinutes => sys::HSM_ALERT_REPEAT_THIRTY_MINUTES,
            AlertRepeat::Hourly => sys::HSM_ALERT_REPEAT_HOURLY,
            AlertRepeat::Daily => sys::HSM_ALERT_REPEAT_DAILY,
            AlertRepeat::Weekly => sys::HSM_ALERT_REPEAT_WEEKLY,
        }
    }
}

/// Seconds from 0001-01-01T00:00:00Z to the Unix epoch.
const SECONDS_FROM_YEAR_ONE_TO_EPOCH: u64 = 62_135_596_800;

/// `0001-01-01T12:00:00Z` — the schedule anchor the managed
/// `ThenSendInstantHourlyScheduledNotification` uses (and the collector's built-in alerts too).
/// Pass it with [`AlertRepeat::Hourly`] and `instant_send = true` to reproduce that notification.
pub fn instant_hourly_schedule_anchor() -> SystemTime {
    // 12 h after the start of year 1. `SystemTime` on Linux is a signed timespec, so a pre-epoch
    // instant is representable; the fallback only exists for a platform where it is not.
    UNIX_EPOCH
        .checked_sub(Duration::from_secs(
            SECONDS_FROM_YEAR_ONE_TO_EPOCH - 12 * 3600,
        ))
        .unwrap_or(UNIX_EPOCH)
}

/// Signed Unix milliseconds, saturating; the ABI takes pre-epoch times as negative values.
fn unix_millis(time: SystemTime) -> i64 {
    match time.duration_since(UNIX_EPOCH) {
        Ok(after) => clamp_millis(after),
        Err(before) => clamp_millis(before.duration()).saturating_neg(),
    }
}

/// A collector-owned alert under construction. Every setter consumes and returns the builder, so a
/// failed step cannot leave a half-built alert in use.
pub struct AlertBuilder<'c> {
    handle: *mut sys::hsm_alert_t,
    collector: &'c Collector,
}

impl std::fmt::Debug for AlertBuilder<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "AlertBuilder({:p})", self.handle)
    }
}

// SAFETY: the builder is the only holder of its handle until `build`, and the setters only write
// the collector-owned alert struct behind it; moving that exclusive access to another thread is
// sound. (Not Sync: the setters mutate.)
unsafe impl Send for AlertBuilder<'_> {}

impl<'c> AlertBuilder<'c> {
    /// # Safety
    /// `handle` must be a live alert created by `collector`.
    pub(crate) unsafe fn from_raw(handle: *mut sys::hsm_alert_t, collector: &'c Collector) -> Self {
        Self { handle, collector }
    }

    /// Append a condition.
    pub fn condition(
        self,
        combination: AlertCombination,
        property: AlertProperty,
        operation: AlertOperation,
        target: AlertTarget,
    ) -> Result<Self> {
        let (target_type, value) = match target {
            AlertTarget::Const(value) => (
                sys::HSM_ALERT_TARGET_CONST,
                Some(CString::new(value).map_err(|err| Error::nul("alert target", err))?),
            ),
            AlertTarget::LastValue => (sys::HSM_ALERT_TARGET_LAST_VALUE, None),
        };
        // SAFETY: live handle owned by us; the comparand outlives the call and is copied.
        let code = unsafe {
            sys::hsm_alert_add_condition(
                self.handle,
                combination.as_raw(),
                property.as_raw(),
                operation.as_raw(),
                target_type,
                value.as_ref().map_or(ptr::null(), |text| text.as_ptr()),
            )
        };
        self.checked("alert add condition", code)
    }

    /// Send a notification when the alert fires (managed `ThenSendNotification`).
    pub fn notification(self, template: &str, destination: AlertDestination) -> Result<Self> {
        let template = cstring("alert template", template)?;
        // SAFETY: live handle; the template outlives the call and is copied.
        let code = unsafe {
            sys::hsm_alert_set_notification(self.handle, template.as_ptr(), destination.as_raw())
        };
        self.checked("alert set notification", code)
    }

    /// Send a scheduled notification (managed `ThenSendScheduledNotification`). For the managed
    /// "instant hourly" variant pass [`instant_hourly_schedule_anchor`], [`AlertRepeat::Hourly`]
    /// and `instant_send = true`.
    pub fn scheduled_notification(
        self,
        template: &str,
        time: SystemTime,
        repeat: AlertRepeat,
        instant_send: bool,
        destination: AlertDestination,
    ) -> Result<Self> {
        let template = cstring("alert template", template)?;
        // SAFETY: live handle; the template outlives the call and is copied.
        let code = unsafe {
            sys::hsm_alert_set_scheduled_notification(
                self.handle,
                template.as_ptr(),
                unix_millis(time),
                repeat.as_raw(),
                instant_send,
                destination.as_raw(),
            )
        };
        self.checked("alert set scheduled notification", code)
    }

    pub fn icon(self, icon: AlertIcon) -> Result<Self> {
        // SAFETY: live handle.
        let code = unsafe { sys::hsm_alert_set_icon(self.handle, icon.as_raw()) };
        self.checked("alert set icon", code)
    }

    /// An arbitrary UTF-8 icon (emoji) instead of a built-in one.
    pub fn icon_raw(self, icon: &str) -> Result<Self> {
        let icon = cstring("alert icon", icon)?;
        // SAFETY: live handle; the text outlives the call and is copied.
        let code = unsafe { sys::hsm_alert_set_icon_raw(self.handle, icon.as_ptr()) };
        self.checked("alert set icon", code)
    }

    /// Raise the sensor to `Error` while the alert holds (managed `AndSetSensorError`). HSM alerts
    /// can only raise `Error`; a "warning" alert is a notification with the warning icon.
    pub fn sensor_error(self) -> Result<Self> {
        // SAFETY: live handle.
        let code = unsafe { sys::hsm_alert_set_sensor_error(self.handle) };
        self.checked("alert set sensor error", code)
    }

    /// Fire only once the condition has held for `period` (managed `AndConfirmationPeriod`).
    pub fn confirmation_period(self, period: Duration) -> Result<Self> {
        // SAFETY: live handle.
        let code =
            unsafe { sys::hsm_alert_set_confirmation_period(self.handle, clamp_millis(period)) };
        self.checked("alert set confirmation period", code)
    }

    /// The inactivity window of a [`AlertKind::Ttl`] alert.
    pub fn inactivity_period(self, period: Duration) -> Result<Self> {
        // SAFETY: live handle.
        let code =
            unsafe { sys::hsm_alert_set_inactivity_period(self.handle, clamp_millis(period)) };
        self.checked("alert set inactivity period", code)
    }

    /// Register the alert disabled (managed `BuildAndDisable`).
    pub fn disabled(self, disabled: bool) -> Result<Self> {
        // SAFETY: live handle.
        let code = unsafe { sys::hsm_alert_set_disabled(self.handle, disabled) };
        self.checked("alert set disabled", code)
    }

    /// Finish the alert. It is attached with `attach_alert` on a sensor handle.
    pub fn build(self) -> Alert<'c> {
        Alert {
            handle: self.handle,
            collector: self.collector,
        }
    }

    fn checked(self, operation: &'static str, code: sys::hsm_result_t) -> Result<Self> {
        if code == sys::HSM_RESULT_OK {
            Ok(self)
        } else {
            // The setters fail only on a NULL handle, which this type cannot hold; reported
            // anyway rather than assumed.
            Err(Error::from_code(operation, code, String::new()))
        }
    }
}

/// A finished alert, owned by the collector. Attach it with `attach_alert` on any sensor handle of
/// the same collector.
pub struct Alert<'c> {
    handle: *mut sys::hsm_alert_t,
    collector: &'c Collector,
}

impl std::fmt::Debug for Alert<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Alert({:p})", self.handle)
    }
}

// SAFETY: a built alert is never written again — attaching only reads (copies) it, and the wrapper
// serializes every attach under the collector's lifecycle lock. The storage is collector-owned and
// outlives `'c`.
unsafe impl Send for Alert<'_> {}
unsafe impl Sync for Alert<'_> {}

impl Alert<'_> {
    pub(crate) fn as_ptr(&self) -> *mut sys::hsm_alert_t {
        self.handle
    }

    pub(crate) fn collector(&self) -> &Collector {
        self.collector
    }
}

fn cstring(field: &'static str, value: &str) -> Result<CString> {
    CString::new(value).map_err(|err| Error::nul(field, err))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_instant_hourly_anchor_is_noon_of_year_one() {
        // DateTime(1, 1, 1, 12, 0, 0, Utc) in the managed BaseConditions, as Unix milliseconds.
        assert_eq!(
            unix_millis(instant_hourly_schedule_anchor()),
            -62_135_553_600_000
        );
    }

    #[test]
    fn unix_millis_is_signed_around_the_epoch() {
        assert_eq!(unix_millis(UNIX_EPOCH), 0);
        assert_eq!(unix_millis(UNIX_EPOCH + Duration::from_millis(1500)), 1500);
        assert_eq!(unix_millis(UNIX_EPOCH - Duration::from_millis(1500)), -1500);
    }
}
