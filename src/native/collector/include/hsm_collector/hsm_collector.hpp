#pragma once

#include "hsm_collector/hsm_collector.h"

#include <stdexcept>
#include <string>
#include <utility>

namespace hsm::collector
{
    class Error : public std::runtime_error
    {
    public:
        explicit Error(const std::string& message)
            : std::runtime_error(message)
        {
        }
    };

    enum class SensorStatus
    {
        OffTime = HSM_SENSOR_STATUS_OFF_TIME,
        Ok = HSM_SENSOR_STATUS_OK,
        Warning = HSM_SENSOR_STATUS_WARNING,
        Error = HSM_SENSOR_STATUS_ERROR,
    };

    struct CollectorOptions
    {
        std::string access_key;
        std::string server_address;
        int port = 443;
        std::string module;
        std::string computer_name;
    };

    class IntSensor
    {
    public:
        explicit IntSensor(hsm_sensor_t* handle = nullptr)
            : handle_(handle)
        {
        }

        IntSensor(const IntSensor&) = delete;
        IntSensor& operator=(const IntSensor&) = delete;

        IntSensor(IntSensor&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr))
        {
        }

        IntSensor& operator=(IntSensor&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                handle_ = std::exchange(other.handle_, nullptr);
            }

            return *this;
        }

        ~IntSensor()
        {
            Reset();
        }

        void AddValue(int value, SensorStatus status = SensorStatus::Ok, const std::string& comment = "")
        {
            const auto result = hsm_sensor_add_int(
                handle_,
                value,
                static_cast<hsm_sensor_status_t>(status),
                comment.c_str());

            if (result != HSM_RESULT_OK)
                throw Error("Failed to add int sensor value.");
        }

    private:
        void Reset()
        {
            hsm_sensor_release(handle_);
            handle_ = nullptr;
        }

        hsm_sensor_t* handle_;
    };

    class Collector
    {
    public:
        explicit Collector(const CollectorOptions& options)
        {
            hsm_collector_options_t native_options{};
            native_options.access_key = options.access_key.c_str();
            native_options.server_address = options.server_address.c_str();
            native_options.port = options.port;
            native_options.module = options.module.c_str();
            native_options.computer_name = options.computer_name.c_str();

            const auto result = hsm_collector_create(&native_options, &handle_);
            if (result != HSM_RESULT_OK)
                throw Error("Failed to create collector.");
        }

        Collector(const Collector&) = delete;
        Collector& operator=(const Collector&) = delete;

        Collector(Collector&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr))
        {
        }

        Collector& operator=(Collector&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                handle_ = std::exchange(other.handle_, nullptr);
            }

            return *this;
        }

        ~Collector()
        {
            Reset();
        }

        void Start()
        {
            Check(hsm_collector_start(handle_));
        }

        void Stop()
        {
            Check(hsm_collector_stop(handle_));
        }

        IntSensor CreateIntSensor(const std::string& path)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(hsm_collector_create_int_sensor(handle_, path.c_str(), &sensor));
            return IntSensor(sensor);
        }

        size_t SentCount() const
        {
            return hsm_collector_sent_count(handle_);
        }

        std::string SentJson(size_t index) const
        {
            const char* json = nullptr;
            Check(hsm_collector_get_sent_json(handle_, index, &json));
            return json == nullptr ? std::string{} : std::string{ json };
        }

        std::string LastError() const
        {
            const auto error = hsm_collector_last_error(handle_);
            return error == nullptr ? std::string{} : std::string{ error };
        }

    private:
        void Check(hsm_result_t result) const
        {
            if (result != HSM_RESULT_OK)
                throw Error(LastError());
        }

        void Reset()
        {
            hsm_collector_destroy(handle_);
            handle_ = nullptr;
        }

        hsm_collector_t* handle_ = nullptr;
    };
}
