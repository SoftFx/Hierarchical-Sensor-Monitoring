#include "hsm_collector/hsm_collector.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <exception>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace
{
    constexpr size_t MaxCommentLength = 1024;

    enum class CollectorState
    {
        Stopped,
        Running,
    };

    std::string CopyString(const char* value)
    {
        return value == nullptr ? std::string{} : std::string{ value };
    }

    std::string TrimComment(std::string comment)
    {
        if (comment.size() > MaxCommentLength)
            comment.resize(MaxCommentLength);

        return comment;
    }

    std::string EscapeJson(const std::string& value)
    {
        std::ostringstream output;

        for (const auto ch : value)
        {
            switch (ch)
            {
            case '\\':
                output << "\\\\";
                break;
            case '"':
                output << "\\\"";
                break;
            case '\b':
                output << "\\b";
                break;
            case '\f':
                output << "\\f";
                break;
            case '\n':
                output << "\\n";
                break;
            case '\r':
                output << "\\r";
                break;
            case '\t':
                output << "\\t";
                break;
            default:
                output << ch;
                break;
            }
        }

        return output.str();
    }

    int64_t UnixTimeMilliseconds()
    {
        const auto now = std::chrono::system_clock::now().time_since_epoch();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    }

    class NativeCollector;

    class NativeSensor
    {
    public:
        NativeSensor(std::weak_ptr<NativeCollector> collector, std::string path, hsm_sensor_type_t type)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(type)
        {
        }

        hsm_result_t AddInt(int32_t value, hsm_sensor_status_t status, const char* comment);

    private:
        std::weak_ptr<NativeCollector> collector_;
        std::string path_;
        hsm_sensor_type_t type_;
    };

    class NativeCollector : public std::enable_shared_from_this<NativeCollector>
    {
    public:
        explicit NativeCollector(const hsm_collector_options_t& options)
            : access_key_(CopyString(options.access_key)),
              server_address_(CopyString(options.server_address)),
              port_(options.port),
              module_(CopyString(options.module)),
              computer_name_(CopyString(options.computer_name))
        {
        }

        hsm_result_t Start()
        {
            std::lock_guard<std::mutex> guard(mutex_);

            if (state_ == CollectorState::Running)
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is already running.");

            state_ = CollectorState::Running;
            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t Stop()
        {
            std::lock_guard<std::mutex> guard(mutex_);

            if (state_ == CollectorState::Stopped)
            {
                ClearError();
                return HSM_RESULT_OK;
            }

            state_ = CollectorState::Stopped;
            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t CreateIntSensor(const char* path, std::shared_ptr<NativeSensor>& out_sensor)
        {
            if (path == nullptr || *path == '\0')
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);

            const auto existing = sensors_.find(path);
            if (existing != sensors_.end())
            {
                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            auto sensor = std::make_shared<NativeSensor>(weak_from_this(), path, HSM_SENSOR_TYPE_INT);
            sensors_.emplace(path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t AddIntValue(
            const std::string& path,
            hsm_sensor_type_t type,
            int32_t value,
            hsm_sensor_status_t status,
            const char* comment)
        {
            std::lock_guard<std::mutex> guard(mutex_);

            if (state_ != CollectorState::Running)
            {
                ClearError();
                return HSM_RESULT_OK;
            }

            const auto normalized_comment = TrimComment(CopyString(comment));

            std::ostringstream json;
            json << "{"
                 << "\"Type\":" << static_cast<int>(type) << ","
                 << "\"Path\":\"" << EscapeJson(path) << "\","
                 << "\"Value\":" << value << ","
                 << "\"Status\":" << static_cast<int>(status) << ","
                 << "\"Comment\":\"" << EscapeJson(normalized_comment) << "\","
                 << "\"UnixTimeMs\":" << UnixTimeMilliseconds()
                 << "}";

            sent_values_.push_back(json.str());
            ClearError();
            return HSM_RESULT_OK;
        }

        size_t SentCount() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return sent_values_.size();
        }

        hsm_result_t SentJson(size_t index, const char** out_json) const
        {
            if (out_json == nullptr)
                return HSM_RESULT_INVALID_ARGUMENT;

            std::lock_guard<std::mutex> guard(mutex_);

            if (index >= sent_values_.size())
            {
                *out_json = nullptr;
                return HSM_RESULT_NOT_FOUND;
            }

            *out_json = sent_values_[index].c_str();
            return HSM_RESULT_OK;
        }

        const char* LastError() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return last_error_.c_str();
        }

    private:
        hsm_result_t SetError(hsm_result_t result, std::string message)
        {
            last_error_ = std::move(message);
            return result;
        }

        void ClearError()
        {
            last_error_.clear();
        }

        mutable std::mutex mutex_;
        CollectorState state_ = CollectorState::Stopped;
        std::unordered_map<std::string, std::shared_ptr<NativeSensor>> sensors_;
        std::vector<std::string> sent_values_;
        std::string last_error_;

        std::string access_key_;
        std::string server_address_;
        int32_t port_;
        std::string module_;
        std::string computer_name_;
    };

    hsm_result_t NativeSensor::AddInt(int32_t value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_INT)
            return HSM_RESULT_INVALID_ARGUMENT;

        const auto collector = collector_.lock();
        if (!collector)
            return HSM_RESULT_INVALID_STATE;

        return collector->AddIntValue(path_, type_, value, status, comment);
    }
}

struct hsm_collector_t
{
    std::shared_ptr<NativeCollector> impl;
};

struct hsm_sensor_t
{
    std::shared_ptr<NativeSensor> impl;
};

hsm_result_t hsm_collector_create(const hsm_collector_options_t* options, hsm_collector_t** out_collector)
{
    if (options == nullptr || out_collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    try
    {
        *out_collector = new hsm_collector_t{ std::make_shared<NativeCollector>(*options) };
        return HSM_RESULT_OK;
    }
    catch (const std::exception&)
    {
        if (out_collector != nullptr)
            *out_collector = nullptr;

        return HSM_RESULT_INTERNAL_ERROR;
    }
}

void hsm_collector_destroy(hsm_collector_t* collector)
{
    delete collector;
}

hsm_result_t hsm_collector_start(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->Start();
}

hsm_result_t hsm_collector_stop(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->Stop();
}

hsm_result_t hsm_collector_create_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateIntSensor(path, sensor);
    if (result != HSM_RESULT_OK)
    {
        *out_sensor = nullptr;
        return result;
    }

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

void hsm_sensor_release(hsm_sensor_t* sensor)
{
    delete sensor;
}

hsm_result_t hsm_sensor_add_int(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddInt(value, status, comment);
}

size_t hsm_collector_sent_count(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return 0;

    return collector->impl->SentCount();
}

hsm_result_t hsm_collector_get_sent_json(const hsm_collector_t* collector, size_t index, const char** out_json)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->SentJson(index, out_json);
}

const char* hsm_collector_last_error(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return "Collector handle is null.";

    return collector->impl->LastError();
}
