#pragma once

/// @file
/// @brief Move-only RAII sensor handles. Each releases its C handle (`hsm_sensor_release`) on
/// destruction; releasing the handle does NOT unregister the sensor (the collector keeps it).

#include "hsm_collector/alerts.hpp"
#include "hsm_collector/enums.hpp"
#include "hsm_collector/error.hpp"
#include "hsm_collector/hsm_collector.h"

#include <chrono>
#include <cstdint>
#include <string>
#include <utility>

namespace hsm::collector
{
    /// Common base: ownership, move semantics, and AttachAlert. Not constructible directly.
    class Sensor
    {
    public:
        Sensor(const Sensor&) = delete;
        Sensor& operator=(const Sensor&) = delete;

        Sensor(Sensor&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr))
        {
        }

        Sensor& operator=(Sensor&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                handle_ = std::exchange(other.handle_, nullptr);
            }

            return *this;
        }

        ~Sensor()
        {
            Reset();
        }

        /// Attach a built alert and rebuild this sensor's registration payload. Attach before the
        /// collector starts (or before the sensor is created while it is already running).
        void AttachAlert(const Alert& alert)
        {
            detail::ThrowIfFailed(hsm_sensor_attach_alert(handle_, alert.handle()), "Failed to attach alert to sensor.");
        }

        /// Underlying C handle (borrowed; still owned by this object).
        hsm_sensor_t* handle() const
        {
            return handle_;
        }

    protected:
        explicit Sensor(hsm_sensor_t* handle)
            : handle_(handle)
        {
        }

        hsm_sensor_t* handle_;

    private:
        void Reset()
        {
            hsm_sensor_release(handle_);
            handle_ = nullptr;
        }
    };

    /// Boolean instant sensor (also the type for a bool last-value sensor).
    class BoolSensor : public Sensor
    {
    public:
        explicit BoolSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(bool value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_bool(handle_, value, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add bool value.");
        }
    };

    /// Integer instant sensor (also the type for an int last-value sensor).
    class IntSensor : public Sensor
    {
    public:
        explicit IntSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(std::int32_t value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_int(handle_, value, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add int value.");
        }
    };

    /// Double instant sensor (also the type for a double last-value sensor).
    class DoubleSensor : public Sensor
    {
    public:
        explicit DoubleSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(double value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_double(handle_, value, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add double value.");
        }
    };

    /// String instant sensor (also the type for a string last-value sensor).
    class StringSensor : public Sensor
    {
    public:
        explicit StringSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(const std::string& value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_string(handle_, value.c_str(), static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add string value.");
        }
    };

    /// Enum instant sensor: AddValue takes the integer enum key (registered via EnumOptions).
    class EnumSensor : public Sensor
    {
    public:
        explicit EnumSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(std::int32_t value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_enum(handle_, value, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add enum value.");
        }
    };

    /// TimeSpan instant sensor. Values are 100-ns ticks; AddValue accepts a chrono duration (lossy
    /// below 100 ns) or pass exact ticks via AddTicks.
    class TimeSpanSensor : public Sensor
    {
    public:
        explicit TimeSpanSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddTicks(std::int64_t ticks, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_timespan(handle_, ticks, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add timespan value.");
        }

        void AddValue(std::chrono::nanoseconds value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            AddTicks(value.count() / 100, status, comment);
        }
    };

    /// Version instant sensor. Pass -1 for an absent build/revision (Version.ToString drops trailing
    /// absent components, so major.minor is the minimum).
    class VersionSensor : public Sensor
    {
    public:
        explicit VersionSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(
            std::int32_t major,
            std::int32_t minor,
            std::int32_t build = -1,
            std::int32_t revision = -1,
            SensorStatus status = SensorStatus::Ok,
            const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_version(handle_, major, minor, build, revision, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add version value.");
        }
    };

    /// Integer bar sensor: AddValue accumulates; AddPartial submits a pre-aggregated bar slice.
    class IntBarSensor : public Sensor
    {
    public:
        explicit IntBarSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(std::int32_t value)
        {
            detail::ThrowIfFailed(hsm_sensor_add_bar_int(handle_, value), "Failed to add int bar value.");
        }

        void AddPartial(std::int32_t min, std::int32_t max, std::int32_t mean, std::int32_t first, std::int32_t last, std::int32_t count)
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_bar_int_partial(handle_, min, max, mean, first, last, count),
                "Failed to add int bar partial.");
        }
    };

    /// Double bar sensor: AddValue accumulates; AddPartial submits a pre-aggregated bar slice.
    class DoubleBarSensor : public Sensor
    {
    public:
        explicit DoubleBarSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(double value)
        {
            detail::ThrowIfFailed(hsm_sensor_add_bar_double(handle_, value), "Failed to add double bar value.");
        }

        void AddPartial(double min, double max, double mean, double first, double last, std::int32_t count)
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_bar_double_partial(handle_, min, max, mean, first, last, count),
                "Failed to add double bar partial.");
        }
    };

    /// Rate sensor: AddValue increments the accumulated sum posted per period as sum/elapsed.
    class RateSensor : public Sensor
    {
    public:
        explicit RateSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(double value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_rate(handle_, value, static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add rate value.");
        }
    };

    /// Pull function sensor: the value comes from the std::function passed at creation; there is no
    /// AddValue. The callable is owned by the collector and outlives this handle.
    class FunctionSensor : public Sensor
    {
    public:
        explicit FunctionSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }
    };

    /// Values-function sensor: AddValue buffers into the sliding window the callback snapshots each
    /// period (the buffer is not drained).
    class ValuesFunctionSensor : public Sensor
    {
    public:
        explicit ValuesFunctionSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddValue(std::int32_t value)
        {
            detail::ThrowIfFailed(hsm_sensor_add_function_int(handle_, value), "Failed to buffer function value.");
        }
    };

    /// File sensor: publishes string content (disk reads are not part of the portable contract).
    class FileSensor : public Sensor
    {
    public:
        explicit FileSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void AddContent(const std::string& content, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            detail::ThrowIfFailed(
                hsm_sensor_add_file(handle_, content.c_str(), static_cast<hsm_sensor_status_t>(status), comment.c_str()),
                "Failed to add file content.");
        }
    };

    /// Service-commands sensor: reports fixed lifecycle commands with an initiator comment.
    class ServiceCommandsSensor : public Sensor
    {
    public:
        explicit ServiceCommandsSensor(hsm_sensor_t* handle = nullptr)
            : Sensor(handle)
        {
        }

        void SendCustom(const std::string& command, const std::string& initiator)
        {
            detail::ThrowIfFailed(
                hsm_service_commands_send_custom(handle_, command.c_str(), initiator.c_str()),
                "Failed to send custom service command.");
        }

        void SendRestart(const std::string& initiator)
        {
            detail::ThrowIfFailed(hsm_service_commands_send_restart(handle_, initiator.c_str()), "Failed to send restart command.");
        }

        void SendStart(const std::string& initiator)
        {
            detail::ThrowIfFailed(hsm_service_commands_send_start(handle_, initiator.c_str()), "Failed to send start command.");
        }

        void SendStop(const std::string& initiator)
        {
            detail::ThrowIfFailed(hsm_service_commands_send_stop(handle_, initiator.c_str()), "Failed to send stop command.");
        }

        void SendUpdate(const std::string& initiator)
        {
            detail::ThrowIfFailed(hsm_service_commands_send_update(handle_, initiator.c_str()), "Failed to send update command.");
        }

        /// `old_version` may be empty to omit the "from <old>" clause.
        void SendUpdateVersion(const std::string& initiator, const std::string& new_version, const std::string& old_version = "")
        {
            detail::ThrowIfFailed(
                hsm_service_commands_send_update_version(
                    handle_,
                    initiator.c_str(),
                    new_version.c_str(),
                    old_version.empty() ? nullptr : old_version.c_str()),
                "Failed to send update-version command.");
        }
    };
} // namespace hsm::collector
