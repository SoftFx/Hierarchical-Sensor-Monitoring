#pragma once

/// @file
/// @brief Strongly-typed C++ mirrors of the frozen C ABI enums.
///
/// Every enumerator carries the SAME numeric value as its `hsm_*_t` counterpart, so a
/// `static_cast` across the boundary is exact and the registration payload stays byte-identical.

#include "hsm_collector/hsm_collector.h"

namespace hsm::collector
{
    /// Sensor value status (mirrors hsm_sensor_status_t / the managed SensorStatus).
    enum class SensorStatus
    {
        OffTime = HSM_SENSOR_STATUS_OFF_TIME,
        Ok = HSM_SENSOR_STATUS_OK,
        Warning = HSM_SENSOR_STATUS_WARNING,
        Error = HSM_SENSOR_STATUS_ERROR,
    };

    /// Collector lifecycle status (mirrors hsm_collector_status_t).
    enum class CollectorStatus
    {
        Stopped = HSM_COLLECTOR_STATUS_STOPPED,
        Starting = HSM_COLLECTOR_STATUS_STARTING,
        Running = HSM_COLLECTOR_STATUS_RUNNING,
        Stopping = HSM_COLLECTOR_STATUS_STOPPING,
        Disposed = HSM_COLLECTOR_STATUS_DISPOSED,
    };

    /// Log level handed to a SetLogger callback (mirrors hsm_log_level_t).
    enum class LogLevel
    {
        Debug = HSM_LOG_LEVEL_DEBUG,
        Info = HSM_LOG_LEVEL_INFO,
        Error = HSM_LOG_LEVEL_ERROR,
    };

    /// Managed Unit codes (mirrors HSMUnit) used by SensorOptions::unit / display_unit.
    enum class Unit
    {
        Bits = 0,
        Bytes = 1,
        KB = 2,
        MB = 3,
        GB = 4,
        Percents = 100,
        Ticks = 1000,
        Milliseconds = 1010,
        Seconds = 1011,
        Minutes = 1012,
    };

    /// Which list an alert lands in (mirrors hsm_alert_kind_t).
    enum class AlertKind
    {
        Instant = HSM_ALERT_KIND_INSTANT,
        Bar = HSM_ALERT_KIND_BAR,
        Ttl = HSM_ALERT_KIND_TTL,
    };

    /// How a condition combines with the previous one (mirrors hsm_alert_combination_t).
    enum class AlertCombination
    {
        And = HSM_ALERT_COMBINATION_AND,
        Or = HSM_ALERT_COMBINATION_OR,
    };

    /// Comparison operator for an alert condition (mirrors hsm_alert_operation_t).
    enum class AlertOperation
    {
        LessThanOrEqual = HSM_ALERT_OP_LESS_THAN_OR_EQUAL,
        LessThan = HSM_ALERT_OP_LESS_THAN,
        GreaterThan = HSM_ALERT_OP_GREATER_THAN,
        GreaterThanOrEqual = HSM_ALERT_OP_GREATER_THAN_OR_EQUAL,
        Equal = HSM_ALERT_OP_EQUAL,
        NotEqual = HSM_ALERT_OP_NOT_EQUAL,
        IsChanged = HSM_ALERT_OP_IS_CHANGED,
        IsError = HSM_ALERT_OP_IS_ERROR,
        IsOk = HSM_ALERT_OP_IS_OK,
        IsChangedToError = HSM_ALERT_OP_IS_CHANGED_TO_ERROR,
        IsChangedToOk = HSM_ALERT_OP_IS_CHANGED_TO_OK,
        Contains = HSM_ALERT_OP_CONTAINS,
        StartsWith = HSM_ALERT_OP_STARTS_WITH,
        EndsWith = HSM_ALERT_OP_ENDS_WITH,
        ReceivedNewValue = HSM_ALERT_OP_RECEIVED_NEW_VALUE,
    };

    /// Which sensor property a condition tests (mirrors hsm_alert_property_t).
    enum class AlertProperty
    {
        Status = HSM_ALERT_PROP_STATUS,
        Comment = HSM_ALERT_PROP_COMMENT,
        Value = HSM_ALERT_PROP_VALUE,
        Min = HSM_ALERT_PROP_MIN,
        Max = HSM_ALERT_PROP_MAX,
        Mean = HSM_ALERT_PROP_MEAN,
        Count = HSM_ALERT_PROP_COUNT,
        LastValue = HSM_ALERT_PROP_LAST_VALUE,
        FirstValue = HSM_ALERT_PROP_FIRST_VALUE,
        Length = HSM_ALERT_PROP_LENGTH,
        OriginalSize = HSM_ALERT_PROP_ORIGINAL_SIZE,
        NewSensorData = HSM_ALERT_PROP_NEW_SENSOR_DATA,
        EmaValue = HSM_ALERT_PROP_EMA_VALUE,
        EmaMin = HSM_ALERT_PROP_EMA_MIN,
        EmaMax = HSM_ALERT_PROP_EMA_MAX,
        EmaMean = HSM_ALERT_PROP_EMA_MEAN,
        EmaCount = HSM_ALERT_PROP_EMA_COUNT,
    };

    /// Comparand kind for a condition (mirrors hsm_alert_target_type_t).
    enum class AlertTargetType
    {
        Const = HSM_ALERT_TARGET_CONST,
        LastValue = HSM_ALERT_TARGET_LAST_VALUE,
    };

    /// Where the alert notification is delivered (mirrors hsm_alert_destination_mode_t).
    enum class AlertDestination
    {
        NotInitialized = HSM_ALERT_DESTINATION_NOT_INITIALIZED,
        Empty = HSM_ALERT_DESTINATION_EMPTY,
        FromParent = HSM_ALERT_DESTINATION_FROM_PARENT,
        AllChats = HSM_ALERT_DESTINATION_ALL_CHATS,
    };

    /// Scheduled-notification repeat cadence (mirrors hsm_alert_repeat_mode_t).
    enum class AlertRepeat
    {
        FiveMinutes = HSM_ALERT_REPEAT_FIVE_MINUTES,
        TenMinutes = HSM_ALERT_REPEAT_TEN_MINUTES,
        FifteenMinutes = HSM_ALERT_REPEAT_FIFTEEN_MINUTES,
        ThirtyMinutes = HSM_ALERT_REPEAT_THIRTY_MINUTES,
        Hourly = HSM_ALERT_REPEAT_HOURLY,
        Daily = HSM_ALERT_REPEAT_DAILY,
        Weekly = HSM_ALERT_REPEAT_WEEKLY,
    };

    /// Built-in alert icon (mirrors hsm_alert_icon_t). Use AlertBuilder::WithIconRaw for an
    /// arbitrary emoji.
    enum class AlertIcon
    {
        Ok = HSM_ALERT_ICON_OK,
        Warning = HSM_ALERT_ICON_WARNING,
        Error = HSM_ALERT_ICON_ERROR,
        Pause = HSM_ALERT_ICON_PAUSE,
        ArrowUp = HSM_ALERT_ICON_ARROW_UP,
        ArrowDown = HSM_ALERT_ICON_ARROW_DOWN,
        Clock = HSM_ALERT_ICON_CLOCK,
        Hourglass = HSM_ALERT_ICON_HOURGLASS,
    };
} // namespace hsm::collector
