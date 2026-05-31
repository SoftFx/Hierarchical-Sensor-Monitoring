#include "hsm_collector/hsm_collector.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <exception>
#include <iomanip>
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

    std::string DoubleJson(double value)
    {
        std::ostringstream output;
        output << std::setprecision(17) << value;
        return output.str();
    }

    struct SensorSnapshot
    {
        std::string path;
        hsm_sensor_type_t type;
        std::string value_json;
        hsm_sensor_status_t status;
        std::string comment;
    };

    class NativeCollector;

    class NativeSensor
    {
    public:
        NativeSensor(
            std::weak_ptr<NativeCollector> collector,
            std::string path,
            hsm_sensor_type_t type,
            bool is_last_value,
            std::string default_value_json)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(type),
              is_last_value_(is_last_value),
              last_value_json_(std::move(default_value_json))
        {
        }

        hsm_result_t AddInt(int32_t value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddBool(bool value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddDouble(double value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddString(const char* value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddEnum(int32_t value, hsm_sensor_status_t status, const char* comment);
        bool TryGetLastValueSnapshot(SensorSnapshot& snapshot) const;
        hsm_sensor_type_t Type() const;
        bool IsLastValue() const;

    private:
        hsm_result_t AddValueJson(std::string value_json, hsm_sensor_status_t status, const char* comment);

        std::weak_ptr<NativeCollector> collector_;
        std::string path_;
        hsm_sensor_type_t type_;
        bool is_last_value_;
        mutable std::mutex mutex_;
        std::string last_value_json_;
        hsm_sensor_status_t last_status_ = HSM_SENSOR_STATUS_OFF_TIME;
        std::string last_comment_;
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

            for (const auto& sensor : sensors_)
            {
                SensorSnapshot snapshot;
                if (sensor.second->TryGetLastValueSnapshot(snapshot))
                    AppendValueJsonNoLock(snapshot.path, snapshot.type, snapshot.value_json, snapshot.status, snapshot.comment.c_str());
            }

            state_ = CollectorState::Stopped;
            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t CreateSensor(
            const char* path,
            hsm_sensor_type_t type,
            bool is_last_value,
            const std::string& default_value_json,
            std::shared_ptr<NativeSensor>& out_sensor)
        {
            if (path == nullptr || *path == '\0')
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);
            const auto sensor_path = BuildSensorPath(path);

            const auto existing = sensors_.find(sensor_path);
            if (existing != sensors_.end())
            {
                if (existing->second->Type() != type || existing->second->IsLastValue() != is_last_value)
                {
                    out_sensor.reset();
                    return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path is already registered with a different type.");
                }

                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            auto sensor = std::make_shared<NativeSensor>(weak_from_this(), sensor_path, type, is_last_value, default_value_json);
            sensors_.emplace(sensor_path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t AddValueJson(
            const std::string& path,
            hsm_sensor_type_t type,
            const std::string& value_json,
            hsm_sensor_status_t status,
            const char* comment)
        {
            std::lock_guard<std::mutex> guard(mutex_);

            if (state_ != CollectorState::Running)
            {
                ClearError();
                return HSM_RESULT_OK;
            }

            AppendValueJsonNoLock(path, type, value_json, status, comment);
            ClearError();
            return HSM_RESULT_OK;
        }

        void AppendValueJsonNoLock(
            const std::string& path,
            hsm_sensor_type_t type,
            const std::string& value_json,
            hsm_sensor_status_t status,
            const char* comment)
        {
            const auto normalized_comment = TrimComment(CopyString(comment));

            std::ostringstream json;
            json << "{"
                 << "\"Type\":" << static_cast<int>(type) << ","
                 << "\"Path\":\"" << EscapeJson(path) << "\","
                 << "\"Value\":" << value_json << ","
                 << "\"Status\":" << static_cast<int>(status) << ","
                 << "\"Comment\":\"" << EscapeJson(normalized_comment) << "\","
                 << "\"UnixTimeMs\":" << UnixTimeMilliseconds()
                 << "}";

            sent_values_.push_back(json.str());
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

        std::string BuildSensorPath(const std::string& path) const
        {
            if (computer_name_.empty() && module_.empty())
                return path;
            if (computer_name_.empty())
                return module_ + "/" + path;
            if (module_.empty())
                return computer_name_ + "/" + path;

            return computer_name_ + "/" + module_ + "/" + path;
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

        return AddValueJson(std::to_string(value), status, comment);
    }

    hsm_result_t NativeSensor::AddBool(bool value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_BOOLEAN)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(value ? "true" : "false", status, comment);
    }

    hsm_result_t NativeSensor::AddDouble(double value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_DOUBLE)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(DoubleJson(value), status, comment);
    }

    hsm_result_t NativeSensor::AddString(const char* value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_STRING)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson("\"" + EscapeJson(CopyString(value)) + "\"", status, comment);
    }

    hsm_result_t NativeSensor::AddEnum(int32_t value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_ENUM)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(std::to_string(value), status, comment);
    }

    hsm_result_t NativeSensor::AddValueJson(std::string value_json, hsm_sensor_status_t status, const char* comment)
    {
        if (is_last_value_)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            last_value_json_ = std::move(value_json);
            last_status_ = status;
            last_comment_ = TrimComment(CopyString(comment));
            return HSM_RESULT_OK;
        }

        const auto collector = collector_.lock();
        if (!collector)
            return HSM_RESULT_INVALID_STATE;

        return collector->AddValueJson(path_, type_, value_json, status, comment);
    }

    bool NativeSensor::TryGetLastValueSnapshot(SensorSnapshot& snapshot) const
    {
        if (!is_last_value_)
            return false;

        std::lock_guard<std::mutex> guard(mutex_);
        snapshot.path = path_;
        snapshot.type = type_;
        snapshot.value_json = last_value_json_;
        snapshot.status = last_status_;
        snapshot.comment = last_comment_;
        return true;
    }

    hsm_sensor_type_t NativeSensor::Type() const
    {
        return type_;
    }

    bool NativeSensor::IsLastValue() const
    {
        return is_last_value_;
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

static hsm_result_t CreateSensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    bool is_last_value,
    const std::string& default_value_json,
    hsm_sensor_t** out_sensor);

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
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_INT, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_BOOLEAN, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_DOUBLE, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_STRING, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_enum_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_ENUM, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_last_value_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int32_t default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_INT, true, std::to_string(default_value), out_sensor);
}

hsm_result_t hsm_collector_create_last_value_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    bool default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_BOOLEAN, true, default_value ? "true" : "false", out_sensor);
}

hsm_result_t hsm_collector_create_last_value_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    double default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_DOUBLE, true, DoubleJson(default_value), out_sensor);
}

hsm_result_t hsm_collector_create_last_value_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    const char* default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(
        collector,
        path,
        HSM_SENSOR_TYPE_STRING,
        true,
        "\"" + EscapeJson(CopyString(default_value)) + "\"",
        out_sensor);
}

static hsm_result_t CreateSensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    bool is_last_value,
    const std::string& default_value_json,
    hsm_sensor_t** out_sensor)
{
    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateSensor(path, type, is_last_value, default_value_json, sensor);
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

hsm_result_t hsm_sensor_add_bool(
    hsm_sensor_t* sensor,
    bool value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBool(value, status, comment);
}

hsm_result_t hsm_sensor_add_double(
    hsm_sensor_t* sensor,
    double value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddDouble(value, status, comment);
}

hsm_result_t hsm_sensor_add_string(
    hsm_sensor_t* sensor,
    const char* value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddString(value, status, comment);
}

hsm_result_t hsm_sensor_add_enum(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddEnum(value, status, comment);
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
