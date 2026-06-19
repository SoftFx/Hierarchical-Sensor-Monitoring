#pragma once

/// @file
/// @brief Fluent alert builder over the C alert DSL (`hsm_collector_create_alert` + `hsm_alert_*`).

#include "hsm_collector/enums.hpp"
#include "hsm_collector/error.hpp"
#include "hsm_collector/hsm_collector.h"

#include <chrono>
#include <cstdint>
#include <string>

namespace hsm::collector
{
    /// A built alert. Non-owning: the handle is owned by the collector that created it and freed
    /// when the collector is destroyed (there is no separate release). Attach it to a sensor with
    /// Sensor::AttachAlert BEFORE the collector starts (or before the sensor is created while the
    /// collector is already running).
    class Alert
    {
    public:
        explicit Alert(hsm_alert_t* handle = nullptr)
            : handle_(handle)
        {
        }

        hsm_alert_t* handle() const
        {
            return handle_;
        }

    private:
        hsm_alert_t* handle_;
    };

    /// Fluent builder mirroring the managed Alerts DSL. Each method mutates the underlying alert in
    /// place and returns `*this`; convert to Alert (implicitly or via Build()) to attach it.
    ///
    /// @code
    ///   auto alert = collector.CreateAlert(AlertKind::Instant)
    ///       .If(AlertProperty::Value, AlertOperation::GreaterThan, "100")
    ///       .ThenNotify("[$product]$path $operation $target")
    ///       .WithIcon(AlertIcon::Warning);
    ///   sensor.AttachAlert(alert);
    /// @endcode
    class AlertBuilder
    {
    public:
        explicit AlertBuilder(hsm_alert_t* handle)
            : handle_(handle)
        {
        }

        /// Condition comparing a property against a constant comparand (the managed DSL stringifies
        /// the comparand).
        AlertBuilder& If(
            AlertProperty property,
            AlertOperation operation,
            const std::string& target,
            AlertCombination combination = AlertCombination::And)
        {
            detail::ThrowIfFailed(
                hsm_alert_add_condition(
                    handle_,
                    static_cast<hsm_alert_combination_t>(combination),
                    static_cast<hsm_alert_property_t>(property),
                    static_cast<hsm_alert_operation_t>(operation),
                    HSM_ALERT_TARGET_CONST,
                    target.c_str()),
                "Failed to add alert condition.");
            return *this;
        }

        /// Unary condition with no comparand (e.g. IsChanged / ReceivedNewValue); serializes
        /// `Value:null`.
        AlertBuilder& If(
            AlertProperty property,
            AlertOperation operation,
            AlertCombination combination = AlertCombination::And)
        {
            detail::ThrowIfFailed(
                hsm_alert_add_condition(
                    handle_,
                    static_cast<hsm_alert_combination_t>(combination),
                    static_cast<hsm_alert_property_t>(property),
                    static_cast<hsm_alert_operation_t>(operation),
                    HSM_ALERT_TARGET_CONST,
                    nullptr),
                "Failed to add alert condition.");
            return *this;
        }

        /// Condition comparing a property against the sensor's own last value.
        AlertBuilder& IfLastValue(
            AlertProperty property,
            AlertOperation operation,
            AlertCombination combination = AlertCombination::And)
        {
            detail::ThrowIfFailed(
                hsm_alert_add_condition(
                    handle_,
                    static_cast<hsm_alert_combination_t>(combination),
                    static_cast<hsm_alert_property_t>(property),
                    static_cast<hsm_alert_operation_t>(operation),
                    HSM_ALERT_TARGET_LAST_VALUE,
                    nullptr),
                "Failed to add alert condition.");
            return *this;
        }

        /// ThenSendNotification: the alert message template + delivery destination.
        AlertBuilder& ThenNotify(
            const std::string& notification_template,
            AlertDestination destination = AlertDestination::FromParent)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_notification(
                    handle_,
                    notification_template.c_str(),
                    static_cast<hsm_alert_destination_mode_t>(destination)),
                "Failed to set alert notification.");
            return *this;
        }

        /// ThenSendScheduledNotification: `time_unix_ms` is serialized as ISO-8601-Z.
        AlertBuilder& ThenScheduledNotify(
            const std::string& notification_template,
            std::int64_t time_unix_ms,
            AlertRepeat repeat,
            bool instant_send,
            AlertDestination destination = AlertDestination::FromParent)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_scheduled_notification(
                    handle_,
                    notification_template.c_str(),
                    time_unix_ms,
                    static_cast<hsm_alert_repeat_mode_t>(repeat),
                    instant_send,
                    static_cast<hsm_alert_destination_mode_t>(destination)),
                "Failed to set scheduled alert notification.");
            return *this;
        }

        /// Built-in icon prepended to the message.
        AlertBuilder& WithIcon(AlertIcon icon)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_icon(handle_, static_cast<hsm_alert_icon_t>(icon)),
                "Failed to set alert icon.");
            return *this;
        }

        /// Arbitrary UTF-8 icon string.
        AlertBuilder& WithIconRaw(const std::string& utf8_icon)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_icon_raw(handle_, utf8_icon.c_str()),
                "Failed to set raw alert icon.");
            return *this;
        }

        /// Raise the alert status to Error (managed AsSensorError).
        AlertBuilder& AsSensorError()
        {
            detail::ThrowIfFailed(hsm_alert_set_sensor_error(handle_), "Failed to set alert sensor-error.");
            return *this;
        }

        /// AndConfirmationPeriod: the condition must hold for this long before the alert fires.
        AlertBuilder& WithConfirmationPeriod(std::chrono::milliseconds period)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_confirmation_period(handle_, static_cast<std::int64_t>(period.count())),
                "Failed to set alert confirmation period.");
            return *this;
        }

        /// BuildAndDisable: register the alert disabled.
        AlertBuilder& Disabled(bool disabled = true)
        {
            detail::ThrowIfFailed(hsm_alert_set_disabled(handle_, disabled), "Failed to set alert disabled flag.");
            return *this;
        }

        /// TTL alerts only: the inactivity window (feeds TTLs/TtlAlerts).
        AlertBuilder& WithInactivityPeriod(std::chrono::milliseconds period)
        {
            detail::ThrowIfFailed(
                hsm_alert_set_inactivity_period(handle_, static_cast<std::int64_t>(period.count())),
                "Failed to set alert inactivity period.");
            return *this;
        }

        /// Finalize to an attachable Alert (also available via implicit conversion).
        Alert Build() const
        {
            return Alert(handle_);
        }

        operator Alert() const
        {
            return Alert(handle_);
        }

        hsm_alert_t* handle() const
        {
            return handle_;
        }

    private:
        hsm_alert_t* handle_;
    };
} // namespace hsm::collector
