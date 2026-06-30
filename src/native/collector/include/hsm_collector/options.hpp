#pragma once

/// @file
/// @brief Strongly-typed sensor registration options that lower to the C `hsm_sensor_options_t`.

#include "hsm_collector/enums.hpp"
#include "hsm_collector/hsm_collector.h"

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>

namespace hsm::collector
{
    /// Path anchor for a non-computer sensor (mirrors the managed SensorLocation;
    /// `hsm_sensor_options_t::sensor_location`).
    enum class SensorLocation
    {
        Module = 0,
        Product = 1,
    };

    /// Registration metadata for an instant sensor (bool/int/double/string/enum/timespan/version).
    /// Every field is optional: an unset field takes the managed default ("emit null"). Lowers to
    /// `hsm_sensor_options_t` starting from `hsm_sensor_options_default()`, so the null/tri-state
    /// sentinels are handled for you.
    ///
    /// Bar sensors are NOT configured through this struct — the C ABI bar factories take their
    /// period/precision directly (see BarOptions); only instant kinds accept the full surface.
    struct SensorOptions
    {
        std::optional<std::chrono::milliseconds> ttl;
        std::optional<Unit> unit;
        std::optional<std::string> description;
        std::optional<std::chrono::milliseconds> keep_history;
        std::optional<std::chrono::milliseconds> self_destroy;
        std::optional<Unit> display_unit;
        /// Enable the EMA statistic (managed StatisticsOptions.EMA). Unset => no statistics.
        std::optional<bool> ema_statistics;
        std::optional<bool> is_singleton;
        std::optional<bool> aggregate_data;
        std::optional<bool> enable_grafana;
        /// Anchor the path at the computer node AND force IsSingletonSensor on the wire.
        bool is_computer_sensor = false;
        SensorLocation location = SensorLocation::Module;
        /// Default-alert suppression (managed DefaultAlertsOptions). None => the server attaches its
        /// default TTL + status-change alerts; combine flags with operator|.
        DefaultAlertsOptions default_alert_options = DefaultAlertsOptions::None;

        /// Lower to the C struct. The returned value borrows `description`'s storage, so this
        /// SensorOptions must outlive the create call that consumes the result (it always does —
        /// the wrapper consumes it inline).
        hsm_sensor_options_t ToNative() const
        {
            hsm_sensor_options_t native = hsm_sensor_options_default();

            if (ttl.has_value())
                native.ttl_ms = static_cast<std::int64_t>(ttl->count());
            if (unit.has_value())
                native.unit = static_cast<std::int32_t>(*unit);
            if (description.has_value())
                native.description = description->c_str();
            if (keep_history.has_value())
                native.keep_history_ms = static_cast<std::int64_t>(keep_history->count());
            if (self_destroy.has_value())
                native.self_destroy_ms = static_cast<std::int64_t>(self_destroy->count());
            if (display_unit.has_value())
                native.display_unit = static_cast<std::int32_t>(*display_unit);
            if (ema_statistics.has_value())
                native.statistics = *ema_statistics ? 1 : 0;
            if (is_singleton.has_value())
                native.is_singleton = *is_singleton ? 1 : 0;
            if (aggregate_data.has_value())
                native.aggregate_data = *aggregate_data ? 1 : 0;
            if (enable_grafana.has_value())
                native.enable_grafana = *enable_grafana ? 1 : 0;

            native.is_computer_sensor = is_computer_sensor;
            native.sensor_location = static_cast<std::int32_t>(location);
            native.default_alert_options = static_cast<std::int64_t>(default_alert_options);

            return native;
        }
    };

    /// Aggregation + registration parameters for a bar sensor (mirrors the managed HSMBarSensorOptions).
    /// `precision` applies to double bars only. The remaining fields are the SensorOptions registration
    /// surface; every one is optional and an unset field takes the managed default. Bars emit
    /// `DisplayUnit:null` unless `display_unit` is set, so a default BarOptions reproduces the plain bar
    /// registration byte-for-byte.
    struct BarOptions
    {
        std::chrono::milliseconds bar_period = std::chrono::minutes(5);
        std::chrono::milliseconds post_period = std::chrono::seconds(15);
        int precision = 2;

        std::optional<std::chrono::milliseconds> ttl;
        std::optional<Unit> unit;
        // No display_unit: managed BarSensorOptions is SensorOptions<NoDisplayUnit> and always
        // registers DisplayUnit:null, so a bar has no display-unit knob to expose.
        std::optional<std::string> description;
        std::optional<std::chrono::milliseconds> keep_history;
        std::optional<std::chrono::milliseconds> self_destroy;
        std::optional<bool> ema_statistics; // managed StatisticsOptions.EMA
        std::optional<bool> is_singleton;
        std::optional<bool> aggregate_data;
        std::optional<bool> enable_grafana;
        bool is_computer_sensor = false;
        SensorLocation location = SensorLocation::Module;
        /// Default-alert suppression (managed DefaultAlertsOptions); combine flags with operator|.
        DefaultAlertsOptions default_alert_options = DefaultAlertsOptions::None;

        /// Lower the registration fields to the C options struct (period/precision are passed
        /// separately to the bar create). The returned value borrows `description`'s storage, so this
        /// BarOptions must outlive the create call that consumes the result (the wrapper consumes it
        /// inline).
        hsm_sensor_options_t ToNative() const
        {
            hsm_sensor_options_t native = hsm_sensor_options_default();

            if (ttl.has_value())
                native.ttl_ms = static_cast<std::int64_t>(ttl->count());
            if (unit.has_value())
                native.unit = static_cast<std::int32_t>(*unit);
            if (description.has_value())
                native.description = description->c_str();
            if (keep_history.has_value())
                native.keep_history_ms = static_cast<std::int64_t>(keep_history->count());
            if (self_destroy.has_value())
                native.self_destroy_ms = static_cast<std::int64_t>(self_destroy->count());
            if (ema_statistics.has_value())
                native.statistics = *ema_statistics ? 1 : 0;
            if (is_singleton.has_value())
                native.is_singleton = *is_singleton ? 1 : 0;
            if (aggregate_data.has_value())
                native.aggregate_data = *aggregate_data ? 1 : 0;
            if (enable_grafana.has_value())
                native.enable_grafana = *enable_grafana ? 1 : 0;

            native.is_computer_sensor = is_computer_sensor;
            native.sensor_location = static_cast<std::int32_t>(location);
            native.default_alert_options = static_cast<std::int64_t>(default_alert_options);

            return native;
        }
    };

    /// Display cadence for a rate sensor's value (mirrors the managed RateDisplayUnit). Lowers to
    /// `hsm_sensor_options_t::display_unit`; an unset RateOptions::display_unit registers PerSecond (0),
    /// matching the managed Convert.ToInt32((RateDisplayUnit?)null) == 0.
    enum class RateDisplayUnit
    {
        PerSecond = 0,
        PerMinute = 1,
        PerHour = 2,
        PerDay = 3,
        PerWeek = 4,
        PerMonth = 5,
    };

    /// Registration + scheduling options for a rate sensor (mirrors the managed RateSensorOptions).
    /// `post_period` is the periodic post cadence (managed PostDataPeriod, default 1 minute). The
    /// remaining fields are the SensorOptions registration surface; every one is optional and an
    /// unset field takes the managed default. Two rate-specific defaults apply at the C ABI when a
    /// field is left unset: OriginalUnit registers ValueInSecond (3000) and DisplayUnit registers 0 —
    /// both are ALWAYS emitted (never null), matching RateSensorOptions.ToApi(). Description stays
    /// null unless set.
    struct RateOptions
    {
        std::chrono::milliseconds post_period = std::chrono::minutes(1);
        std::optional<std::chrono::milliseconds> ttl;
        std::optional<Unit> unit;                    // OriginalUnit; unset => ValueInSecond (3000)
        std::optional<RateDisplayUnit> display_unit; // unset => PerSecond (0)
        std::optional<std::string> description;      // unset => null (managed RateSensorOptions.Description default)
        std::optional<std::chrono::milliseconds> keep_history;
        std::optional<std::chrono::milliseconds> self_destroy;
        std::optional<bool> ema_statistics; // managed StatisticsOptions.EMA
        std::optional<bool> is_singleton;
        std::optional<bool> aggregate_data;
        std::optional<bool> enable_grafana;
        bool is_computer_sensor = false;
        SensorLocation location = SensorLocation::Module;
        /// Default-alert suppression (managed DefaultAlertsOptions); combine flags with operator|.
        DefaultAlertsOptions default_alert_options = DefaultAlertsOptions::None;

        /// Lower to the C options struct. `unit`/`display_unit` are left at the "unset" sentinel when
        /// not set so the C ABI applies the rate defaults (3000 / 0). The returned value borrows
        /// `description`'s storage, so this RateOptions must outlive the create call that consumes the
        /// result (the wrapper consumes it inline).
        hsm_sensor_options_t ToNative() const
        {
            hsm_sensor_options_t native = hsm_sensor_options_default();

            if (ttl.has_value())
                native.ttl_ms = static_cast<std::int64_t>(ttl->count());
            if (unit.has_value())
                native.unit = static_cast<std::int32_t>(*unit);
            if (display_unit.has_value())
                native.display_unit = static_cast<std::int32_t>(*display_unit);
            if (description.has_value())
                native.description = description->c_str();
            if (keep_history.has_value())
                native.keep_history_ms = static_cast<std::int64_t>(keep_history->count());
            if (self_destroy.has_value())
                native.self_destroy_ms = static_cast<std::int64_t>(self_destroy->count());
            if (ema_statistics.has_value())
                native.statistics = *ema_statistics ? 1 : 0;
            if (is_singleton.has_value())
                native.is_singleton = *is_singleton ? 1 : 0;
            if (aggregate_data.has_value())
                native.aggregate_data = *aggregate_data ? 1 : 0;
            if (enable_grafana.has_value())
                native.enable_grafana = *enable_grafana ? 1 : 0;

            native.is_computer_sensor = is_computer_sensor;
            native.sensor_location = static_cast<std::int32_t>(location);
            native.default_alert_options = static_cast<std::int64_t>(default_alert_options);

            return native;
        }
    };

    /// One entry of an enum sensor's EnumOptions table (mirrors hsm_enum_option_t): the integer
    /// key, its display value, an ARGB color, and an optional markdown description.
    struct EnumOption
    {
        std::int32_t key = 0;
        std::string value;
        std::int32_t color = 0;
        std::string description;
    };
} // namespace hsm::collector
