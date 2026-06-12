#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"
#include <iostream>
#include <fstream>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <functional>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace
{
    void Require(bool condition, const char* message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

    void Contains(const std::string& value, const std::string& expected)
    {
        if (value.find(expected) == std::string::npos)
            throw std::runtime_error("Expected substring not found: " + expected + "\nActual: " + value);
    }

    void NotContains(const std::string& value, const std::string& unexpected)
    {
        if (value.find(unexpected) != std::string::npos)
            throw std::runtime_error("Unexpected substring found: " + unexpected + "\nActual: " + value);
    }

    hsm_collector_options_t TestOptions()
    {
        hsm_collector_options_t options{};
        options.access_key = "test-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "conformance-module";
        options.computer_name = "conformance-host";
        return options;
    }

    struct CollectorHandle
    {
        hsm_collector_t* value = nullptr;

        CollectorHandle() = default;

        explicit CollectorHandle(hsm_collector_t* handle)
            : value(handle)
        {
        }

        CollectorHandle(const CollectorHandle&) = delete;
        CollectorHandle& operator=(const CollectorHandle&) = delete;

        CollectorHandle(CollectorHandle&& other) noexcept
            : value(std::exchange(other.value, nullptr))
        {
        }

        CollectorHandle& operator=(CollectorHandle&& other) noexcept
        {
            if (this != &other)
            {
                hsm_collector_destroy(value);
                value = std::exchange(other.value, nullptr);
            }

            return *this;
        }

        ~CollectorHandle()
        {
            hsm_collector_destroy(value);
        }
    };

    struct SensorHandle
    {
        hsm_sensor_t* value = nullptr;

        SensorHandle() = default;

        explicit SensorHandle(hsm_sensor_t* handle)
            : value(handle)
        {
        }

        SensorHandle(const SensorHandle&) = delete;
        SensorHandle& operator=(const SensorHandle&) = delete;

        SensorHandle(SensorHandle&& other) noexcept
            : value(std::exchange(other.value, nullptr))
        {
        }

        SensorHandle& operator=(SensorHandle&& other) noexcept
        {
            if (this != &other)
            {
                hsm_sensor_release(value);
                value = std::exchange(other.value, nullptr);
            }

            return *this;
        }

        ~SensorHandle()
        {
            hsm_sensor_release(value);
        }
    };

    CollectorHandle CreateCollector()
    {
        CollectorHandle collector;
        auto options = TestOptions();

        Require(hsm_collector_create(&options, &collector.value) == HSM_RESULT_OK, "collector create failed");

        return collector;
    }

    CollectorHandle CreateCollector(const hsm_collector_options_t& options)
    {
        CollectorHandle collector;

        Require(hsm_collector_create(&options, &collector.value) == HSM_RESULT_OK, "collector create failed");

        return collector;
    }

    SensorHandle CreateIntSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_int_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "sensor create failed");

        return sensor;
    }

    SensorHandle CreateBoolSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_bool_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "bool sensor create failed");

        return sensor;
    }

    SensorHandle CreateDoubleSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_double_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "double sensor create failed");

        return sensor;
    }

    SensorHandle CreateStringSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_string_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "string sensor create failed");

        return sensor;
    }

    SensorHandle CreateEnumSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_enum_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "enum sensor create failed");

        return sensor;
    }

    SensorHandle CreateLastIntSensor(hsm_collector_t* collector, const char* path, int default_value)
    {
        SensorHandle sensor;

        Require(
            hsm_collector_create_last_value_int_sensor(collector, path, default_value, &sensor.value) == HSM_RESULT_OK,
            "last int sensor create failed");

        return sensor;
    }

    SensorHandle CreateLastBoolSensor(hsm_collector_t* collector, const char* path, bool default_value)
    {
        SensorHandle sensor;

        Require(
            hsm_collector_create_last_value_bool_sensor(collector, path, default_value, &sensor.value) == HSM_RESULT_OK,
            "last bool sensor create failed");

        return sensor;
    }

    SensorHandle CreateLastDoubleSensor(hsm_collector_t* collector, const char* path, double default_value)
    {
        SensorHandle sensor;

        Require(
            hsm_collector_create_last_value_double_sensor(collector, path, default_value, &sensor.value) == HSM_RESULT_OK,
            "last double sensor create failed");

        return sensor;
    }

    SensorHandle CreateLastStringSensor(hsm_collector_t* collector, const char* path, const char* default_value)
    {
        SensorHandle sensor;

        Require(
            hsm_collector_create_last_value_string_sensor(collector, path, default_value, &sensor.value) == HSM_RESULT_OK,
            "last string sensor create failed");

        return sensor;
    }

    std::string SentJson(hsm_collector_t* collector, size_t index)
    {
        const char* json = nullptr;

        Require(hsm_collector_get_sent_json(collector, index, &json) == HSM_RESULT_OK, "payload lookup failed");

        return std::string{ json };
    }

    // Dispatch is asynchronous (worker thread + collect period), so count asserts poll with a
    // deadline — exact-equality semantics matching the C# harness's WaitForCountAsync.
    bool WaitForSentCountEquals(hsm_collector_t* collector, size_t expected, int timeout_ms = 2000)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);

        while (std::chrono::steady_clock::now() < deadline)
        {
            if (hsm_collector_sent_count(collector) == expected)
                return true;

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        return hsm_collector_sent_count(collector) == expected;
    }

    bool WaitForSentCountAtLeast(hsm_collector_t* collector, size_t minimum, int timeout_ms)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);

        while (std::chrono::steady_clock::now() < deadline)
        {
            if (hsm_collector_sent_count(collector) >= minimum)
                return true;

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        return hsm_collector_sent_count(collector) >= minimum;
    }

    std::string WaitForFirstPayload(hsm_collector_t* collector)
    {
        Require(WaitForSentCountEquals(collector, 1), "payload was not dispatched in time");
        return SentJson(collector, 0);
    }

    std::string NumberFieldFromPayload(const std::string& payload, const std::string& field)
    {
        const auto key = "\"" + field + "\":";
        const auto start = payload.find(key);
        Require(start != std::string::npos, ("payload should include field " + field + "\nActual: " + payload).c_str());

        const auto value_start = start + key.size();
        const auto end = payload.find_first_of(",}", value_start);
        Require(end != std::string::npos, "payload field should be terminated");

        return payload.substr(value_start, end - value_start);
    }

    bool IsBarPayload(const std::string& payload)
    {
        return payload.find("\"OpenTimeMs\":") != std::string::npos;
    }

    std::string CommentFromPayload(const std::string& payload)
    {
        const std::string prefix = "\"Comment\":\"";
        const auto start = payload.find(prefix);
        Require(start != std::string::npos, "payload should include Comment");

        const auto value_start = start + prefix.size();
        const auto end = payload.find('"', value_start);
        Require(end != std::string::npos, "payload Comment should be closed");

        return payload.substr(value_start, end - value_start);
    }

    std::vector<std::string> SplitLine(const std::string& line)
    {
        std::vector<std::string> parts;
        std::string part;
        std::istringstream input(line);

        while (std::getline(input, part, '|'))
            parts.push_back(part);

        return parts;
    }

    int ToInt(const std::string& value)
    {
        return std::stoi(value);
    }

    bool ToBool(const std::string& value)
    {
        if (value == "true" || value == "True")
            return true;
        if (value == "false" || value == "False")
            return false;

        throw std::runtime_error("Unknown bool: " + value);
    }

    double ToDouble(const std::string& value)
    {
        if (value == "NaN")
            return std::numeric_limits<double>::quiet_NaN();
        if (value == "Infinity")
            return std::numeric_limits<double>::infinity();
        if (value == "-Infinity")
            return -std::numeric_limits<double>::infinity();

        return std::stod(value);
    }

    hsm_sensor_status_t ToStatus(const std::string& value)
    {
        if (value == "OffTime")
            return HSM_SENSOR_STATUS_OFF_TIME;
        if (value == "Ok")
            return HSM_SENSOR_STATUS_OK;
        if (value == "Warning")
            return HSM_SENSOR_STATUS_WARNING;
        if (value == "Error")
            return HSM_SENSOR_STATUS_ERROR;

        throw std::runtime_error("Unknown status: " + value);
    }

    hsm_sensor_status_t ToRawStatus(const std::string& value)
    {
        return static_cast<hsm_sensor_status_t>(std::stoi(value));
    }

    std::string ExpandTextToken(const std::string& value)
    {
        const std::string repeat_prefix = "repeat:";
        if (value.rfind(repeat_prefix, 0) != 0)
        {
            if (value == "token:json-special")
                return "quote\"slash\\tab\tnewline\n";
            if (value == "token:control-01")
                return std::string("a") + static_cast<char>(0x01) + "b";
            if (value == "token:control-02")
                return std::string("path") + static_cast<char>(0x02) + "part";
            if (value == "token:control-1f")
                return std::string("bad") + static_cast<char>(0x1f) + "comment";
            if (value == "token:blank")
                return " \t ";
            if (value == "token:null")
                return std::string{};

            return value;
        }

        const auto char_start = repeat_prefix.size();
        const auto separator = value.find(':', char_start);
        Require(separator != std::string::npos && separator > char_start, "repeat token should include a character and count");

        const auto ch = value[char_start];
        const auto count = static_cast<size_t>(std::stoul(value.substr(separator + 1)));
        return std::string(count, ch);
    }

    struct ConformanceState
    {
        CollectorHandle collector;
        std::vector<SensorHandle> sensors;

        struct MixedInstantSet
        {
            size_t bool_index;
            size_t int_index;
            size_t double_index;
            size_t string_index;
            size_t enum_index;
        };

        std::vector<MixedInstantSet> mixed_sets;

        // Keeps function-sensor constants alive for the lifetime of the case (the C API stores
        // the raw user_data pointer).
        std::vector<std::unique_ptr<int32_t>> function_constants;
    };

    int32_t ConstantIntFunction(void* user_data)
    {
        return *static_cast<int32_t*>(user_data);
    }

    int32_t SumIntValuesFunction(const int32_t* values, int32_t count, void* /*user_data*/)
    {
        long long sum = 0;

        for (int32_t index = 0; index < count; ++index)
            sum += values[index];

        return static_cast<int32_t>(sum);
    }

    // Scans the numeric `"Value":` field of a payload; quoted (string/file) values don't parse
    // and are skipped by the caller.
    bool TryNumericValueFromPayload(const std::string& payload, double& out_value)
    {
        const std::string key = "\"Value\":";
        const auto start = payload.find(key);
        if (start == std::string::npos)
            return false;

        const auto value_start = start + key.size();
        if (value_start >= payload.size() || payload[value_start] == '"')
            return false;

        const auto end = payload.find_first_of(",}", value_start);
        if (end == std::string::npos)
            return false;

        try
        {
            out_value = std::stod(payload.substr(value_start, end - value_start));
            return true;
        }
        catch (const std::exception&)
        {
            return false;
        }
    }

    using StepsByCase = std::map<std::string, std::vector<std::vector<std::string>>>;

    StepsByCase ReadConformanceFile(const std::string& path)
    {
        std::ifstream input(path);
        if (!input)
            throw std::runtime_error("Cannot open conformance fixture: " + path);

        StepsByCase cases;
        std::string line;

        while (std::getline(input, line))
        {
            // Fixtures may be checked out with CRLF endings (git autocrlf on Windows);
            // std::getline keeps the '\r' on POSIX, which would corrupt the last field.
            if (!line.empty() && line.back() == '\r')
                line.pop_back();

            if (line.empty() || line[0] == '#')
                continue;

            auto parts = SplitLine(line);
            if (parts.size() < 2)
                throw std::runtime_error("Invalid conformance line: " + line);

            auto case_name = parts[0];
            parts.erase(parts.begin());
            cases[case_name].push_back(std::move(parts));
        }

        return cases;
    }

    void ExecuteConformanceStep(ConformanceState& state, const std::vector<std::string>& step)
    {
        const auto& action = step[0];

        if (action == "create_collector")
        {
            state.collector = CreateCollector();
            return;
        }

        if (action == "create_collector_with_identity")
        {
            Require(step.size() >= 3, "create_collector_with_identity requires computer name and module");
            auto options = TestOptions();
            const auto computer_name = ExpandTextToken(step[1]);
            const auto module = ExpandTextToken(step[2]);
            options.computer_name = computer_name.c_str();
            options.module = module.c_str();
            state.collector = CreateCollector(options);
            return;
        }

        if (action == "expect_create_collector_rejected")
        {
            Require(step.size() >= 3, "expect_create_collector_rejected requires access key and server address");
            auto options = TestOptions();
            const auto access_key = ExpandTextToken(step[1]);
            const auto server_address = ExpandTextToken(step[2]);
            options.access_key = access_key.c_str();
            options.server_address = server_address.c_str();
            if (step.size() >= 4)
                options.port = ToInt(step[3]);

            hsm_collector_t* collector = nullptr;
            const auto result = hsm_collector_create(&options, &collector);
            if (result == HSM_RESULT_OK)
            {
                hsm_collector_destroy(collector);
                throw std::runtime_error("collector create unexpectedly succeeded");
            }

            Require(collector == nullptr, "rejected collector create returned a handle");
            return;
        }

        if (action == "start")
        {
            Require(hsm_collector_start(state.collector.value) == HSM_RESULT_OK, "collector start failed");
            return;
        }

        if (action == "stop")
        {
            Require(hsm_collector_stop(state.collector.value) == HSM_RESULT_OK, "collector stop failed");
            return;
        }

        if (action == "create_int_sensor")
        {
            Require(step.size() >= 2, "create_int_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            state.sensors.push_back(CreateIntSensor(state.collector.value, path.c_str()));
            return;
        }

        if (action == "create_bool_sensor")
        {
            Require(step.size() >= 2, "create_bool_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            state.sensors.push_back(CreateBoolSensor(state.collector.value, path.c_str()));
            return;
        }

        if (action == "create_double_sensor")
        {
            Require(step.size() >= 2, "create_double_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            state.sensors.push_back(CreateDoubleSensor(state.collector.value, path.c_str()));
            return;
        }

        if (action == "create_string_sensor")
        {
            Require(step.size() >= 2, "create_string_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            state.sensors.push_back(CreateStringSensor(state.collector.value, path.c_str()));
            return;
        }

        if (action == "create_enum_sensor")
        {
            Require(step.size() >= 2, "create_enum_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            state.sensors.push_back(CreateEnumSensor(state.collector.value, path.c_str()));
            return;
        }

        if (action == "create_last_int_sensor")
        {
            Require(step.size() >= 3, "create_last_int_sensor requires path and default value");
            state.sensors.push_back(CreateLastIntSensor(state.collector.value, step[1].c_str(), ToInt(step[2])));
            return;
        }

        if (action == "create_last_bool_sensor")
        {
            Require(step.size() >= 3, "create_last_bool_sensor requires path and default value");
            state.sensors.push_back(CreateLastBoolSensor(state.collector.value, step[1].c_str(), ToBool(step[2])));
            return;
        }

        if (action == "create_last_double_sensor")
        {
            Require(step.size() >= 3, "create_last_double_sensor requires path and default value");
            state.sensors.push_back(CreateLastDoubleSensor(state.collector.value, step[1].c_str(), ToDouble(step[2])));
            return;
        }

        if (action == "create_last_string_sensor")
        {
            Require(step.size() >= 3, "create_last_string_sensor requires path and default value");
            const auto default_value = ExpandTextToken(step[2]);
            state.sensors.push_back(CreateLastStringSensor(state.collector.value, step[1].c_str(), default_value.c_str()));
            return;
        }

        if (action == "create_int_sensors")
        {
            Require(step.size() >= 3, "create_int_sensors requires count and path prefix");
            const auto count = ToInt(step[1]);

            for (int i = 0; i < count; ++i)
                state.sensors.push_back(CreateIntSensor(state.collector.value, (step[2] + "/" + std::to_string(i)).c_str()));

            return;
        }

        if (action == "create_mixed_instant_sensors")
        {
            Require(step.size() >= 3, "create_mixed_instant_sensors requires count and path prefix");
            const auto count = ToInt(step[1]);

            for (int i = 0; i < count; ++i)
            {
                const auto prefix = step[2] + "/" + std::to_string(i);
                ConformanceState::MixedInstantSet set{};

                set.bool_index = state.sensors.size();
                state.sensors.push_back(CreateBoolSensor(state.collector.value, (prefix + "/bool").c_str()));

                set.int_index = state.sensors.size();
                state.sensors.push_back(CreateIntSensor(state.collector.value, (prefix + "/int").c_str()));

                set.double_index = state.sensors.size();
                state.sensors.push_back(CreateDoubleSensor(state.collector.value, (prefix + "/double").c_str()));

                set.string_index = state.sensors.size();
                state.sensors.push_back(CreateStringSensor(state.collector.value, (prefix + "/string").c_str()));

                set.enum_index = state.sensors.size();
                state.sensors.push_back(CreateEnumSensor(state.collector.value, (prefix + "/enum").c_str()));

                state.mixed_sets.push_back(set);
            }

            return;
        }

        if (action == "expect_create_int_sensor_rejected")
        {
            Require(step.size() >= 2, "expect_create_int_sensor_rejected requires path");
            hsm_sensor_t* sensor = nullptr;
            const auto result = hsm_collector_create_int_sensor(state.collector.value, step[1].c_str(), &sensor);

            if (result == HSM_RESULT_OK)
            {
                hsm_sensor_release(sensor);
                throw std::runtime_error("int sensor create unexpectedly succeeded");
            }

            Require(sensor == nullptr, "rejected int sensor create returned a handle");
            return;
        }

        if (action == "expect_create_last_int_sensor_rejected")
        {
            Require(step.size() >= 3, "expect_create_last_int_sensor_rejected requires path and default value");
            hsm_sensor_t* sensor = nullptr;
            const auto result = hsm_collector_create_last_value_int_sensor(
                state.collector.value,
                step[1].c_str(),
                ToInt(step[2]),
                &sensor);

            if (result == HSM_RESULT_OK)
            {
                hsm_sensor_release(sensor);
                throw std::runtime_error("last int sensor create unexpectedly succeeded");
            }

            Require(sensor == nullptr, "rejected last int sensor create returned a handle");
            return;
        }

        if (action == "expect_create_last_bool_sensor_rejected")
        {
            Require(step.size() >= 3, "expect_create_last_bool_sensor_rejected requires path and default value");
            hsm_sensor_t* sensor = nullptr;
            const auto result = hsm_collector_create_last_value_bool_sensor(
                state.collector.value,
                step[1].c_str(),
                ToBool(step[2]),
                &sensor);

            if (result == HSM_RESULT_OK)
            {
                hsm_sensor_release(sensor);
                throw std::runtime_error("last bool sensor create unexpectedly succeeded");
            }

            Require(sensor == nullptr, "rejected last bool sensor create returned a handle");
            return;
        }

        if (action == "expect_create_last_double_sensor_rejected")
        {
            Require(step.size() >= 3, "expect_create_last_double_sensor_rejected requires path and default value");
            hsm_sensor_t* sensor = nullptr;
            const auto result = hsm_collector_create_last_value_double_sensor(
                state.collector.value,
                step[1].c_str(),
                ToDouble(step[2]),
                &sensor);

            if (result == HSM_RESULT_OK)
            {
                hsm_sensor_release(sensor);
                throw std::runtime_error("last double sensor create unexpectedly succeeded");
            }

            Require(sensor == nullptr, "rejected last double sensor create returned a handle");
            return;
        }

        if (action == "expect_create_last_string_sensor_rejected")
        {
            Require(step.size() >= 3, "expect_create_last_string_sensor_rejected requires path and default value");
            hsm_sensor_t* sensor = nullptr;
            const auto default_value = ExpandTextToken(step[2]);
            const char* default_value_ptr = step[2] == "token:null" ? nullptr : default_value.c_str();
            const auto result = hsm_collector_create_last_value_string_sensor(
                state.collector.value,
                step[1].c_str(),
                default_value_ptr,
                &sensor);

            if (result == HSM_RESULT_OK)
            {
                hsm_sensor_release(sensor);
                throw std::runtime_error("last string sensor create unexpectedly succeeded");
            }

            Require(sensor == nullptr, "rejected last string sensor create returned a handle");
            return;
        }

        if (action == "add_int")
        {
            Require(step.size() >= 5, "add_int requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_int(state.sensors[sensor_index].value, ToInt(step[2]), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_int failed");
            return;
        }

        if (action == "add_bool")
        {
            Require(step.size() >= 5, "add_bool requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_bool(state.sensors[sensor_index].value, ToBool(step[2]), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_bool failed");
            return;
        }

        if (action == "add_double")
        {
            Require(step.size() >= 5, "add_double requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_double(state.sensors[sensor_index].value, ToDouble(step[2]), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_double failed");
            return;
        }

        if (action == "add_string")
        {
            Require(step.size() >= 5, "add_string requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            const auto value = ExpandTextToken(step[2]);
            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_string(state.sensors[sensor_index].value, value.c_str(), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_string failed");
            return;
        }

        if (action == "add_enum")
        {
            Require(step.size() >= 5, "add_enum requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_enum(state.sensors[sensor_index].value, ToInt(step[2]), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_enum failed");
            return;
        }

        if (action == "expect_add_int_rejected")
        {
            Require(step.size() >= 5, "expect_add_int_rejected requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto before = hsm_collector_sent_count(state.collector.value);
            const auto comment = ExpandTextToken(step[4]);

            Require(
                hsm_sensor_add_int(state.sensors[sensor_index].value, ToInt(step[2]), ToRawStatus(step[3]), comment.c_str()) != HSM_RESULT_OK,
                "invalid int add unexpectedly succeeded");
            Require(hsm_collector_sent_count(state.collector.value) == before, "rejected int add should not send");
            return;
        }

        if (action == "expect_add_bool_rejected")
        {
            Require(step.size() >= 5, "expect_add_bool_rejected requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto before = hsm_collector_sent_count(state.collector.value);
            const auto comment = ExpandTextToken(step[4]);

            Require(
                hsm_sensor_add_bool(state.sensors[sensor_index].value, ToBool(step[2]), ToRawStatus(step[3]), comment.c_str()) != HSM_RESULT_OK,
                "invalid bool add unexpectedly succeeded");
            Require(hsm_collector_sent_count(state.collector.value) == before, "rejected bool add should not send");
            return;
        }

        if (action == "expect_add_double_rejected")
        {
            Require(step.size() >= 5, "expect_add_double_rejected requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto before = hsm_collector_sent_count(state.collector.value);
            const auto comment = ExpandTextToken(step[4]);

            Require(
                hsm_sensor_add_double(state.sensors[sensor_index].value, ToDouble(step[2]), ToRawStatus(step[3]), comment.c_str()) !=
                    HSM_RESULT_OK,
                "invalid double add unexpectedly succeeded");
            Require(hsm_collector_sent_count(state.collector.value) == before, "rejected double add should not send");
            return;
        }

        if (action == "expect_add_string_rejected")
        {
            Require(step.size() >= 5, "expect_add_string_rejected requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto before = hsm_collector_sent_count(state.collector.value);
            const auto value = ExpandTextToken(step[2]);
            const auto comment = ExpandTextToken(step[4]);
            const char* value_ptr = step[2] == "token:null" ? nullptr : value.c_str();

            Require(
                hsm_sensor_add_string(state.sensors[sensor_index].value, value_ptr, ToRawStatus(step[3]), comment.c_str()) !=
                    HSM_RESULT_OK,
                "invalid string add unexpectedly succeeded");
            Require(hsm_collector_sent_count(state.collector.value) == before, "rejected string add should not send");
            return;
        }

        if (action == "expect_add_enum_rejected")
        {
            Require(step.size() >= 5, "expect_add_enum_rejected requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto before = hsm_collector_sent_count(state.collector.value);
            const auto comment = ExpandTextToken(step[4]);

            Require(
                hsm_sensor_add_enum(state.sensors[sensor_index].value, ToInt(step[2]), ToRawStatus(step[3]), comment.c_str()) !=
                    HSM_RESULT_OK,
                "invalid enum add unexpectedly succeeded");
            Require(hsm_collector_sent_count(state.collector.value) == before, "rejected enum add should not send");
            return;
        }

        if (action == "dispose_sensor")
        {
            Require(step.size() >= 2, "dispose_sensor requires sensor index");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            hsm_sensor_release(state.sensors[sensor_index].value);
            state.sensors[sensor_index].value = nullptr;
            return;
        }

        if (action == "add_int_sequence")
        {
            Require(step.size() >= 6, "add_int_sequence requires sensor count, values per sensor, start value, status, and comment");
            const auto sensor_count = ToInt(step[1]);
            const auto values_per_sensor = ToInt(step[2]);
            const auto start_value = ToInt(step[3]);
            const auto status = ToStatus(step[4]);
            const auto comment = ExpandTextToken(step[5]);

            Require(sensor_count <= static_cast<int>(state.sensors.size()), "sensor count out of range");

            for (int sensor_index = 0; sensor_index < sensor_count; ++sensor_index)
            {
                for (int value_index = 0; value_index < values_per_sensor; ++value_index)
                {
                    const auto value = start_value + sensor_index * values_per_sensor + value_index;
                    Require(
                        hsm_sensor_add_int(state.sensors[static_cast<size_t>(sensor_index)].value, value, status, comment.c_str()) == HSM_RESULT_OK,
                        "add_int_sequence failed");
                }
            }

            return;
        }

        if (action == "add_int_parallel")
        {
            Require(step.size() >= 6, "add_int_parallel requires worker count, values per worker, sensor count, status, and comment");
            const auto worker_count = ToInt(step[1]);
            const auto values_per_worker = ToInt(step[2]);
            const auto sensor_count = ToInt(step[3]);
            const auto status = ToStatus(step[4]);
            const auto comment = ExpandTextToken(step[5]);

            Require(sensor_count <= static_cast<int>(state.sensors.size()), "sensor count out of range");

            std::vector<std::thread> workers;
            std::exception_ptr first_exception;
            std::mutex exception_mutex;

            for (int worker = 0; worker < worker_count; ++worker)
            {
                workers.emplace_back([&, worker]()
                {
                    try
                    {
                        for (int value_index = 0; value_index < values_per_worker; ++value_index)
                        {
                            const auto sensor_index = static_cast<size_t>((worker + value_index) % sensor_count);
                            const auto value = worker * values_per_worker + value_index;
                            Require(
                                hsm_sensor_add_int(state.sensors[sensor_index].value, value, status, comment.c_str()) == HSM_RESULT_OK,
                                "add_int_parallel failed");
                        }
                    }
                    catch (...)
                    {
                        std::lock_guard<std::mutex> guard(exception_mutex);
                        if (!first_exception)
                            first_exception = std::current_exception();
                    }
                });
            }

            for (auto& worker : workers)
                worker.join();

            if (first_exception)
                std::rethrow_exception(first_exception);

            return;
        }

        const auto add_mixed_instant_value = [&](size_t set_index, int value, hsm_sensor_status_t status, const std::string& comment)
        {
            Require(set_index < state.mixed_sets.size(), "mixed set index out of range");
            const auto& set = state.mixed_sets[set_index];
            const auto string_value = "value-" + std::to_string(value);

            Require(
                hsm_sensor_add_bool(state.sensors[set.bool_index].value, value % 2 == 0, status, comment.c_str()) == HSM_RESULT_OK,
                "mixed bool add failed");
            Require(
                hsm_sensor_add_int(state.sensors[set.int_index].value, value, status, comment.c_str()) == HSM_RESULT_OK,
                "mixed int add failed");
            Require(
                hsm_sensor_add_double(state.sensors[set.double_index].value, value + 0.25, status, comment.c_str()) == HSM_RESULT_OK,
                "mixed double add failed");
            Require(
                hsm_sensor_add_string(state.sensors[set.string_index].value, string_value.c_str(), status, comment.c_str()) == HSM_RESULT_OK,
                "mixed string add failed");
            Require(
                hsm_sensor_add_enum(state.sensors[set.enum_index].value, value % 4, status, comment.c_str()) == HSM_RESULT_OK,
                "mixed enum add failed");
        };

        if (action == "add_mixed_instant_sequence")
        {
            Require(step.size() >= 6, "add_mixed_instant_sequence requires set count, values per set, start value, status, and comment");
            const auto set_count = ToInt(step[1]);
            const auto values_per_set = ToInt(step[2]);
            const auto start_value = ToInt(step[3]);
            const auto status = ToStatus(step[4]);
            const auto comment = ExpandTextToken(step[5]);

            Require(set_count <= static_cast<int>(state.mixed_sets.size()), "mixed set count out of range");

            for (int set_index = 0; set_index < set_count; ++set_index)
            {
                for (int value_index = 0; value_index < values_per_set; ++value_index)
                {
                    const auto value = start_value + set_index * values_per_set + value_index;
                    add_mixed_instant_value(static_cast<size_t>(set_index), value, status, comment);
                }
            }

            return;
        }

        if (action == "add_mixed_instant_parallel")
        {
            Require(step.size() >= 6, "add_mixed_instant_parallel requires worker count, values per worker, set count, status, and comment");
            const auto worker_count = ToInt(step[1]);
            const auto values_per_worker = ToInt(step[2]);
            const auto set_count = ToInt(step[3]);
            const auto status = ToStatus(step[4]);
            const auto comment = ExpandTextToken(step[5]);

            Require(set_count <= static_cast<int>(state.mixed_sets.size()), "mixed set count out of range");

            std::vector<std::thread> workers;
            std::exception_ptr first_exception;
            std::mutex exception_mutex;

            for (int worker = 0; worker < worker_count; ++worker)
            {
                workers.emplace_back([&, worker]()
                {
                    try
                    {
                        for (int value_index = 0; value_index < values_per_worker; ++value_index)
                        {
                            const auto set_index = static_cast<size_t>((worker + value_index) % set_count);
                            const auto value = worker * values_per_worker + value_index;
                            add_mixed_instant_value(set_index, value, status, comment);
                        }
                    }
                    catch (...)
                    {
                        std::lock_guard<std::mutex> guard(exception_mutex);
                        if (!first_exception)
                            first_exception = std::current_exception();
                    }
                });
            }

            for (auto& worker : workers)
                worker.join();

            if (first_exception)
                std::rethrow_exception(first_exception);

            return;
        }

        if (action == "expect_conflicting_mixed_creates_rejected_parallel")
        {
            Require(
                step.size() >= 4,
                "expect_conflicting_mixed_creates_rejected_parallel requires worker count, path count, and path prefix");

            const auto worker_count = ToInt(step[1]);
            const auto path_count = ToInt(step[2]);
            const auto path_prefix = step[3];

            const auto expect_rejected = [](hsm_result_t result, hsm_sensor_t* sensor)
            {
                if (result == HSM_RESULT_OK)
                {
                    hsm_sensor_release(sensor);
                    throw std::runtime_error("conflicting sensor create succeeded");
                }

                Require(sensor == nullptr, "conflicting sensor create returned a handle");
            };

            std::vector<std::thread> workers;
            std::exception_ptr first_exception;
            std::mutex exception_mutex;

            for (int worker = 0; worker < worker_count; ++worker)
            {
                workers.emplace_back([&, worker]()
                {
                    try
                    {
                        for (int path_index = worker; path_index < path_count; path_index += worker_count)
                        {
                            const auto path = path_prefix + "/" + std::to_string(path_index);
                            hsm_sensor_t* sensor = nullptr;

                            expect_rejected(
                                hsm_collector_create_bool_sensor(state.collector.value, path.c_str(), &sensor),
                                sensor);

                            sensor = nullptr;
                            expect_rejected(
                                hsm_collector_create_double_sensor(state.collector.value, path.c_str(), &sensor),
                                sensor);

                            sensor = nullptr;
                            expect_rejected(
                                hsm_collector_create_string_sensor(state.collector.value, path.c_str(), &sensor),
                                sensor);

                            sensor = nullptr;
                            expect_rejected(
                                hsm_collector_create_enum_sensor(state.collector.value, path.c_str(), &sensor),
                                sensor);
                        }
                    }
                    catch (...)
                    {
                        std::lock_guard<std::mutex> guard(exception_mutex);
                        if (!first_exception)
                            first_exception = std::current_exception();
                    }
                });
            }

            for (auto& worker : workers)
                worker.join();

            if (first_exception)
                std::rethrow_exception(first_exception);

            return;
        }

        if (action == "repeat_start_stop_add")
        {
            Require(step.size() >= 5, "repeat_start_stop_add requires cycles, sensor index, status, and comment prefix");
            const auto cycles = ToInt(step[1]);
            const auto sensor_index = static_cast<size_t>(ToInt(step[2]));
            const auto status = ToStatus(step[3]);
            const auto comment_prefix = ExpandTextToken(step[4]);

            Require(sensor_index < state.sensors.size(), "sensor index out of range");

            for (int cycle = 0; cycle < cycles; ++cycle)
            {
                Require(hsm_collector_start(state.collector.value) == HSM_RESULT_OK, "repeat start failed");

                const auto comment = comment_prefix + "-" + std::to_string(cycle);
                Require(
                    hsm_sensor_add_int(state.sensors[sensor_index].value, cycle, status, comment.c_str()) == HSM_RESULT_OK,
                    "repeat add failed");

                Require(hsm_collector_stop(state.collector.value) == HSM_RESULT_OK, "repeat stop failed");
            }

            return;
        }

        if (action == "expect_sent_count")
        {
            Require(step.size() >= 2, "expect_sent_count requires count");
            const auto expected = static_cast<size_t>(ToInt(step[1]));
            const auto timeout_ms = step.size() >= 3 ? ToInt(step[2]) * 1000 : 2000;

            Require(
                WaitForSentCountEquals(state.collector.value, expected, timeout_ms),
                ("sent count did not match: expected " + step[1] +
                 ", got " + std::to_string(hsm_collector_sent_count(state.collector.value))).c_str());
            return;
        }

        if (action == "expect_sent_count_between")
        {
            Require(step.size() >= 4, "expect_sent_count_between requires min, max, and timeout");
            const auto minimum = static_cast<size_t>(ToInt(step[1]));
            const auto maximum = static_cast<size_t>(ToInt(step[2]));

            Require(
                WaitForSentCountAtLeast(state.collector.value, minimum, ToInt(step[3]) * 1000),
                "sent count did not reach the expected minimum");

            const auto count = hsm_collector_sent_count(state.collector.value);
            Require(count <= maximum, ("sent count exceeded the expected maximum: " + std::to_string(count)).c_str());
            return;
        }

        if (action == "expect_payload_contains")
        {
            Require(step.size() >= 3, "expect_payload_contains requires index and substring");
            const auto payload = SentJson(state.collector.value, static_cast<size_t>(ToInt(step[1])));
            Contains(payload, step[2]);
            return;
        }

        if (action == "expect_payload_not_contains")
        {
            Require(step.size() >= 3, "expect_payload_not_contains requires index and substring");
            const auto payload = SentJson(state.collector.value, static_cast<size_t>(ToInt(step[1])));
            NotContains(payload, step[2]);
            return;
        }

        if (action == "expect_comment_length")
        {
            Require(step.size() >= 3, "expect_comment_length requires index and length");
            const auto payload = SentJson(state.collector.value, static_cast<size_t>(ToInt(step[1])));
            const auto comment = CommentFromPayload(payload);
            Require(comment.size() == static_cast<size_t>(ToInt(step[2])), "comment length did not match");
            return;
        }

        if (action == "expect_all_payloads_contain")
        {
            Require(step.size() >= 2, "expect_all_payloads_contain requires substring");
            const auto count = hsm_collector_sent_count(state.collector.value);

            for (size_t index = 0; index < count; ++index)
                Contains(SentJson(state.collector.value, index), step[1]);

            return;
        }

        if (action == "expect_payload_value_sequence")
        {
            Require(step.size() >= 4, "expect_payload_value_sequence requires start payload index, count, and start value");
            const auto start_payload_index = static_cast<size_t>(ToInt(step[1]));
            const auto count = ToInt(step[2]);
            const auto start_value = ToInt(step[3]);

            for (int offset = 0; offset < count; ++offset)
            {
                const auto payload = SentJson(state.collector.value, start_payload_index + static_cast<size_t>(offset));
                Contains(payload, "\"Value\":" + std::to_string(start_value + offset));
            }

            return;
        }

        if (action == "expect_payload_type_counts")
        {
            Require(step.size() >= 6, "expect_payload_type_counts requires bool, int, double, string, and enum counts");

            const auto count_type = [&](const std::string& type)
            {
                auto actual = 0;
                const auto sent_count = hsm_collector_sent_count(state.collector.value);

                for (size_t index = 0; index < sent_count; ++index)
                {
                    if (SentJson(state.collector.value, index).find(type) != std::string::npos)
                        actual++;
                }

                return actual;
            };

            Require(count_type("\"Type\":0,") == ToInt(step[1]), "bool type count did not match");
            Require(count_type("\"Type\":1,") == ToInt(step[2]), "int type count did not match");
            Require(count_type("\"Type\":2,") == ToInt(step[3]), "double type count did not match");
            Require(count_type("\"Type\":3,") == ToInt(step[4]), "string type count did not match");
            Require(count_type("\"Type\":10,") == ToInt(step[5]), "enum type count did not match");
            return;
        }

        if (action == "create_collector_with_limits")
        {
            Require(step.size() >= 4, "create_collector_with_limits requires max queue, max package, and collect period");
            auto options = TestOptions();
            options.max_queue_size = ToInt(step[1]);
            options.max_values_in_package = ToInt(step[2]);
            options.package_collect_period_ms = ToInt(step[3]);
            state.collector = CreateCollector(options);
            return;
        }

        if (action == "create_int_bar_sensor")
        {
            Require(step.size() >= 4, "create_int_bar_sensor requires path, bar period, and post period");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(
                hsm_collector_create_int_bar_sensor(
                    state.collector.value, path.c_str(), std::stoll(step[2]), std::stoll(step[3]), &sensor.value) == HSM_RESULT_OK,
                "int bar sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_double_bar_sensor")
        {
            Require(step.size() >= 5, "create_double_bar_sensor requires path, bar period, post period, and precision");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(
                hsm_collector_create_double_bar_sensor(
                    state.collector.value, path.c_str(), std::stoll(step[2]), std::stoll(step[3]), ToInt(step[4]), &sensor.value) == HSM_RESULT_OK,
                "double bar sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "add_bar_int")
        {
            Require(step.size() >= 3, "add_bar_int requires sensor index and value");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            Require(
                hsm_sensor_add_bar_int(state.sensors[sensor_index].value, ToInt(step[2])) == HSM_RESULT_OK,
                "add_bar_int failed");
            return;
        }

        if (action == "add_bar_double")
        {
            Require(step.size() >= 3, "add_bar_double requires sensor index and value");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            Require(
                hsm_sensor_add_bar_double(state.sensors[sensor_index].value, ToDouble(step[2])) == HSM_RESULT_OK,
                "add_bar_double failed");
            return;
        }

        if (action == "add_bar_int_sequence")
        {
            Require(step.size() >= 5, "add_bar_int_sequence requires sensor index, count, start value, and step");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto count = ToInt(step[2]);
            const auto start_value = ToInt(step[3]);
            const auto value_step = ToInt(step[4]);

            for (int index = 0; index < count; ++index)
                Require(
                    hsm_sensor_add_bar_int(state.sensors[sensor_index].value, start_value + index * value_step) == HSM_RESULT_OK,
                    "add_bar_int_sequence failed");

            return;
        }

        if (action == "add_bar_int_parallel")
        {
            Require(step.size() >= 5, "add_bar_int_parallel requires sensor index, worker count, values per worker, and start value");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto worker_count = ToInt(step[2]);
            const auto values_per_worker = ToInt(step[3]);
            const auto start_value = ToInt(step[4]);

            std::vector<std::thread> workers;
            std::exception_ptr first_exception;
            std::mutex exception_mutex;

            for (int worker = 0; worker < worker_count; ++worker)
            {
                workers.emplace_back([&, worker]()
                {
                    try
                    {
                        for (int value_index = 0; value_index < values_per_worker; ++value_index)
                        {
                            const auto value = start_value + worker * values_per_worker + value_index;
                            Require(
                                hsm_sensor_add_bar_int(state.sensors[sensor_index].value, value) == HSM_RESULT_OK,
                                "add_bar_int_parallel failed");
                        }
                    }
                    catch (...)
                    {
                        std::lock_guard<std::mutex> guard(exception_mutex);
                        if (!first_exception)
                            first_exception = std::current_exception();
                    }
                });
            }

            for (auto& worker : workers)
                worker.join();

            if (first_exception)
                std::rethrow_exception(first_exception);

            return;
        }

        if (action == "add_int_bar_partial")
        {
            Require(step.size() >= 8, "add_int_bar_partial requires sensor index, min, max, mean, first, last, and count");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            Require(
                hsm_sensor_add_bar_int_partial(
                    state.sensors[sensor_index].value,
                    ToInt(step[2]), ToInt(step[3]), ToInt(step[4]), ToInt(step[5]), ToInt(step[6]), ToInt(step[7])) == HSM_RESULT_OK,
                "add_int_bar_partial failed");
            return;
        }

        if (action == "add_double_bar_partial")
        {
            Require(step.size() >= 8, "add_double_bar_partial requires sensor index, min, max, mean, first, last, and count");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            Require(
                hsm_sensor_add_bar_double_partial(
                    state.sensors[sensor_index].value,
                    ToDouble(step[2]), ToDouble(step[3]), ToDouble(step[4]), ToDouble(step[5]), ToDouble(step[6]), ToInt(step[7])) == HSM_RESULT_OK,
                "add_double_bar_partial failed");
            return;
        }

        if (action == "sleep_ms")
        {
            Require(step.size() >= 2, "sleep_ms requires milliseconds");
            std::this_thread::sleep_for(std::chrono::milliseconds(ToInt(step[1])));
            return;
        }

        if (action == "set_sender_fail_next")
        {
            Require(step.size() >= 2, "set_sender_fail_next requires count");
            hsm_collector_set_send_fail_next(state.collector.value, ToInt(step[1]));
            return;
        }

        if (action == "set_sender_hang")
        {
            hsm_collector_set_send_hang(state.collector.value, true);
            return;
        }

        if (action == "stop_expect_under_ms")
        {
            Require(step.size() >= 2, "stop_expect_under_ms requires a bound in milliseconds");

            const auto started = std::chrono::steady_clock::now();
            Require(hsm_collector_stop(state.collector.value) == HSM_RESULT_OK, "collector stop failed");
            const auto elapsed_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::steady_clock::now() - started).count();

            Require(
                elapsed_ms < ToInt(step[1]),
                ("stop took " + std::to_string(elapsed_ms) + " ms, expected under " + step[1] + " ms").c_str());
            return;
        }

        if (action == "expect_bar_field")
        {
            Require(step.size() >= 4, "expect_bar_field requires payload index, field, and expected value");

            const auto raw_index = ToInt(step[1]);
            const auto sent_count = hsm_collector_sent_count(state.collector.value);
            size_t payload_index;
            if (raw_index < 0)
            {
                Require(static_cast<size_t>(-raw_index) <= sent_count, "negative payload index out of range");
                payload_index = sent_count - static_cast<size_t>(-raw_index);
            }
            else
            {
                payload_index = static_cast<size_t>(raw_index);
            }

            const auto payload = SentJson(state.collector.value, payload_index);
            const auto& field = step[2];

            static const std::map<std::string, std::string> field_keys = {
                { "type", "Type" }, { "min", "Min" }, { "max", "Max" }, { "mean", "Mean" },
                { "first", "First" }, { "last", "Last" }, { "count", "Count" }, { "status", "Status" },
            };

            const auto key = field_keys.find(field);
            Require(key != field_keys.end(), ("unknown bar field: " + field).c_str());

            const auto actual_text = NumberFieldFromPayload(payload, key->second);

            if (field == "type" || field == "count" || field == "status")
            {
                Require(
                    std::stoll(actual_text) == static_cast<long long>(ToInt(step[3])),
                    ("bar field " + field + " mismatch: expected " + step[3] + ", got " + actual_text).c_str());
            }
            else
            {
                // Tolerant numeric compare (relative 1e-9) — sidesteps double formatting
                // differences between the C# and C++ payload texts.
                const auto actual = std::stod(actual_text);
                const auto expected = ToDouble(step[3]);
                const auto tolerance = std::max(1e-12, std::abs(expected) * 1e-9);

                Require(
                    std::abs(actual - expected) <= tolerance,
                    ("bar field " + field + " mismatch: expected " + step[3] + ", got " + actual_text).c_str());
            }

            return;
        }

        if (action == "expect_bar_open_close_aligned")
        {
            Require(step.size() >= 3, "expect_bar_open_close_aligned requires payload index and period");
            const auto payload = SentJson(state.collector.value, static_cast<size_t>(ToInt(step[1])));
            const auto period = std::stoll(step[2]);
            const auto open = std::stoll(NumberFieldFromPayload(payload, "OpenTimeMs"));
            const auto close = std::stoll(NumberFieldFromPayload(payload, "CloseTimeMs"));

            Require(close - open == period, "bar close - open should equal the bar period");
            Require(open % period == 0, "bar open time should be aligned to the bar period");
            return;
        }

        if (action == "expect_all_bars_aligned")
        {
            Require(step.size() >= 2, "expect_all_bars_aligned requires period");
            const auto period = std::stoll(step[1]);
            const auto sent_count = hsm_collector_sent_count(state.collector.value);
            size_t bars = 0;

            for (size_t index = 0; index < sent_count; ++index)
            {
                const auto payload = SentJson(state.collector.value, index);
                if (!IsBarPayload(payload))
                    continue;

                ++bars;
                const auto open = std::stoll(NumberFieldFromPayload(payload, "OpenTimeMs"));
                const auto close = std::stoll(NumberFieldFromPayload(payload, "CloseTimeMs"));
                Require(close - open == period, "bar close - open should equal the bar period");
                Require(open % period == 0, "bar open time should be aligned to the bar period");
            }

            Require(bars > 0, "expected at least one bar payload");
            return;
        }

        if (action == "expect_bar_open_times_increasing")
        {
            const auto sent_count = hsm_collector_sent_count(state.collector.value);
            long long previous_open = -1;
            bool has_previous = false;

            for (size_t index = 0; index < sent_count; ++index)
            {
                const auto payload = SentJson(state.collector.value, index);
                if (!IsBarPayload(payload))
                    continue;

                const auto open = std::stoll(NumberFieldFromPayload(payload, "OpenTimeMs"));

                if (has_previous)
                    Require(previous_open < open, "bar open times should be strictly increasing");

                previous_open = open;
                has_previous = true;
            }

            return;
        }

        if (action == "expect_bar_count_total")
        {
            Require(step.size() >= 2, "expect_bar_count_total requires expected total");
            const auto sent_count = hsm_collector_sent_count(state.collector.value);
            long long total = 0;

            for (size_t index = 0; index < sent_count; ++index)
            {
                const auto payload = SentJson(state.collector.value, index);
                if (IsBarPayload(payload))
                    total += std::stoll(NumberFieldFromPayload(payload, "Count"));
            }

            Require(
                total == static_cast<long long>(ToInt(step[1])),
                ("bar count total mismatch: expected " + step[1] + ", got " + std::to_string(total)).c_str());
            return;
        }

        if (action == "expect_each_value_once")
        {
            Require(step.size() >= 3, "expect_each_value_once requires start value and count");
            const auto start_value = ToInt(step[1]);
            const auto count = ToInt(step[2]);
            const auto sent_count = hsm_collector_sent_count(state.collector.value);

            std::vector<long long> values;
            for (size_t index = 0; index < sent_count; ++index)
            {
                const auto payload = SentJson(state.collector.value, index);
                if (payload.find("\"Type\":1,") != std::string::npos)
                    values.push_back(std::stoll(NumberFieldFromPayload(payload, "Value")));
            }

            Require(values.size() == static_cast<size_t>(count), "int payload count should equal the expected value count");

            const std::set<long long> distinct(values.begin(), values.end());
            Require(distinct.size() == values.size(), "values should be delivered without duplicates");

            for (int value = start_value; value < start_value + count; ++value)
                Require(distinct.count(value) == 1, ("missing value: " + std::to_string(value)).c_str());

            return;
        }

        if (action == "create_rate_sensor")
        {
            Require(step.size() >= 3, "create_rate_sensor requires path and post period");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(
                hsm_collector_create_rate_sensor(state.collector.value, path.c_str(), std::stoll(step[2]), &sensor.value) == HSM_RESULT_OK,
                "rate sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "add_rate")
        {
            Require(step.size() >= 5, "add_rate requires sensor index, value, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_rate(state.sensors[sensor_index].value, ToDouble(step[2]), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_rate failed");
            return;
        }

        if (action == "add_rate_raw")
        {
            Require(step.size() >= 5, "add_rate_raw requires sensor index, value, raw status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_rate(state.sensors[sensor_index].value, ToDouble(step[2]), ToRawStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_rate_raw should be a silent no-op");
            return;
        }

        if (action == "create_function_int_sensor")
        {
            Require(step.size() >= 4, "create_function_int_sensor requires path, post period, and constant");
            const auto path = ExpandTextToken(step[1]);
            state.function_constants.push_back(std::unique_ptr<int32_t>(new int32_t(ToInt(step[3]))));
            SensorHandle sensor;
            Require(
                hsm_collector_create_function_int_sensor(
                    state.collector.value,
                    path.c_str(),
                    std::stoll(step[2]),
                    &ConstantIntFunction,
                    state.function_constants.back().get(),
                    &sensor.value) == HSM_RESULT_OK,
                "function sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_values_function_int_sum_sensor")
        {
            Require(step.size() >= 4, "create_values_function_int_sum_sensor requires path, post period, and max cache size");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(
                hsm_collector_create_values_function_int_sensor(
                    state.collector.value,
                    path.c_str(),
                    std::stoll(step[2]),
                    ToInt(step[3]),
                    &SumIntValuesFunction,
                    nullptr,
                    &sensor.value) == HSM_RESULT_OK,
                "values function sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "add_function_value")
        {
            Require(step.size() >= 3, "add_function_value requires sensor index and value");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            Require(
                hsm_sensor_add_function_int(state.sensors[sensor_index].value, ToInt(step[2])) == HSM_RESULT_OK,
                "add_function_value failed");
            return;
        }

        if (action == "create_file_sensor")
        {
            Require(step.size() >= 4, "create_file_sensor requires path, default file name, and extension");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(
                hsm_collector_create_file_sensor(
                    state.collector.value, path.c_str(), step[2].c_str(), step[3].c_str(), &sensor.value) == HSM_RESULT_OK,
                "file sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "add_file_value")
        {
            Require(step.size() >= 5, "add_file_value requires sensor index, content, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto content = ExpandTextToken(step[2]);
            const char* content_ptr = step[2] == "token:null" ? nullptr : content.c_str();
            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_file(state.sensors[sensor_index].value, content_ptr, ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_file_value failed");
            return;
        }

        if (action == "expect_eventually_payload_contains")
        {
            Require(step.size() >= 3, "expect_eventually_payload_contains requires substring and timeout");
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(ToInt(step[2]));

            while (std::chrono::steady_clock::now() < deadline)
            {
                const auto count = hsm_collector_sent_count(state.collector.value);

                for (size_t index = 0; index < count; ++index)
                    if (SentJson(state.collector.value, index).find(step[1]) != std::string::npos)
                        return;

                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }

            throw std::runtime_error("No payload contained '" + step[1] + "' within the timeout");
        }

        if (action == "expect_eventually_value_above")
        {
            Require(step.size() >= 3, "expect_eventually_value_above requires threshold and timeout");
            const auto threshold = ToDouble(step[1]);
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(ToInt(step[2]));

            while (std::chrono::steady_clock::now() < deadline)
            {
                const auto count = hsm_collector_sent_count(state.collector.value);

                for (size_t index = 0; index < count; ++index)
                {
                    double numeric = 0;
                    if (TryNumericValueFromPayload(SentJson(state.collector.value, index), numeric) && numeric > threshold)
                        return;
                }

                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }

            throw std::runtime_error("No payload value above " + step[1] + " within the timeout");
        }

        if (action == "expect_no_new_payloads_for_ms")
        {
            Require(step.size() >= 2, "expect_no_new_payloads_for_ms requires milliseconds");
            const auto baseline = hsm_collector_sent_count(state.collector.value);
            std::this_thread::sleep_for(std::chrono::milliseconds(ToInt(step[1])));
            const auto current = hsm_collector_sent_count(state.collector.value);
            Require(
                current == baseline,
                ("expected no new payloads, baseline " + std::to_string(baseline) + ", got " + std::to_string(current)).c_str());
            return;
        }

        throw std::runtime_error("Unknown conformance action: " + action);
    }

    void RunConformanceContract(const std::string& path)
    {
        const auto cases = ReadConformanceFile(path);
        Require(!cases.empty(), "conformance fixture should contain cases");

        for (const auto& test_case : cases)
        {
            ConformanceState state;

            try
            {
                for (const auto& step : test_case.second)
                    ExecuteConformanceStep(state, step);
            }
            catch (const std::exception& ex)
            {
                throw std::runtime_error("Conformance case '" + test_case.first + "' failed: " + ex.what());
            }
        }
    }

    void NativeInvalidArgumentClearsOutParams()
    {
        auto* collector = reinterpret_cast<hsm_collector_t*>(static_cast<uintptr_t>(1));
        Require(
            hsm_collector_create(nullptr, &collector) == HSM_RESULT_INVALID_ARGUMENT,
            "collector create with null options should fail");
        Require(collector == nullptr, "collector create failure should clear out collector");

        auto* sensor = reinterpret_cast<hsm_sensor_t*>(static_cast<uintptr_t>(1));
        Require(
            hsm_collector_create_int_sensor(nullptr, "native/api/null-collector", &sensor) == HSM_RESULT_INVALID_ARGUMENT,
            "sensor create with null collector should fail");
        Require(sensor == nullptr, "sensor create failure should clear out sensor");

        const char* json = reinterpret_cast<const char*>(static_cast<uintptr_t>(1));
        Require(
            hsm_collector_get_sent_json(nullptr, 0, &json) == HSM_RESULT_INVALID_ARGUMENT,
            "sent json with null collector should fail");
        Require(json == nullptr, "sent json failure should clear out json");
    }

    void NativeAddAfterCollectorDestroyIsRejected()
    {
        auto options = TestOptions();
        hsm_collector_t* collector = nullptr;
        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create failed");

        hsm_sensor_t* instant = nullptr;
        hsm_sensor_t* last_value = nullptr;

        Require(
            hsm_collector_create_int_sensor(collector, "native/api/destroy/instant", &instant) == HSM_RESULT_OK,
            "instant sensor create failed");
        Require(
            hsm_collector_create_last_value_int_sensor(collector, "native/api/destroy/last", 0, &last_value) == HSM_RESULT_OK,
            "last-value sensor create failed");

        hsm_collector_destroy(collector);

        Require(
            hsm_sensor_add_int(instant, 1, HSM_SENSOR_STATUS_OK, "after-destroy") == HSM_RESULT_INVALID_STATE,
            "instant sensor add after collector destroy should fail");
        Require(
            hsm_sensor_add_int(last_value, 1, HSM_SENSOR_STATUS_OK, "after-destroy") == HSM_RESULT_INVALID_STATE,
            "last-value sensor add after collector destroy should fail");

        hsm_sensor_release(instant);
        hsm_sensor_release(last_value);
    }

    void NativeSentJsonFailureReportsFreshError()
    {
        auto options = TestOptions();
        hsm_collector_t* collector = nullptr;
        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create failed");

        hsm_sensor_t* sensor = nullptr;
        Require(
            hsm_collector_create_int_sensor(collector, "", &sensor) == HSM_RESULT_INVALID_ARGUMENT,
            "empty sensor path should fail");
        Require(sensor == nullptr, "empty sensor path failure should clear out sensor");

        const auto stale_error = std::string{ hsm_collector_last_error(collector) };
        Require(!stale_error.empty(), "empty sensor path should set last error");

        const char* json = reinterpret_cast<const char*>(static_cast<uintptr_t>(1));
        Require(
            hsm_collector_get_sent_json(collector, 0, &json) == HSM_RESULT_NOT_FOUND,
            "missing sent json should return not found");
        Require(json == nullptr, "missing sent json should clear out json");

        const auto fresh_error = std::string{ hsm_collector_last_error(collector) };
        Require(!fresh_error.empty(), "missing sent json should set last error");
        Require(fresh_error != stale_error, "missing sent json should not leave a stale last error");

        hsm_collector_destroy(collector);
    }

    void NativeWrapperSentJsonMissingThrowsMessage()
    {
        hsm::collector_spike::CollectorOptions options;
        options.access_key = "test-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "native-wrapper";
        options.computer_name = "native-host";

        hsm::collector_spike::Collector collector(options);

        try
        {
            (void)collector.SentJson(0);
        }
        catch (const hsm::collector_spike::Error& ex)
        {
            Require(std::string{ ex.what() }.find("not found") != std::string::npos, "wrapper error should explain missing sent value");
            return;
        }

        throw std::runtime_error("wrapper sent json should throw for missing payload");
    }

    void ExpectCollectorCreateRejected(hsm_collector_options_t options)
    {
        auto* collector = reinterpret_cast<hsm_collector_t*>(static_cast<uintptr_t>(1));
        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_INVALID_ARGUMENT, "collector create should fail");
        Require(collector == nullptr, "rejected collector create should clear out collector");
    }

    void NativeCreateRejectsNullServerAddress()
    {
        auto options = TestOptions();
        options.server_address = nullptr;
        ExpectCollectorCreateRejected(options);
    }

    void NativeCreateRejectsBlankServerAddress()
    {
        auto options = TestOptions();
        options.server_address = " \t ";
        ExpectCollectorCreateRejected(options);
    }

    void NativeCreateRejectsNullAccessKey()
    {
        auto options = TestOptions();
        options.access_key = nullptr;
        ExpectCollectorCreateRejected(options);
    }

    void NativeCreateRejectsBlankAccessKey()
    {
        auto options = TestOptions();
        options.access_key = " \r\n ";
        ExpectCollectorCreateRejected(options);
    }

    void NativeSlashOnlyModuleIsOmittedFromPayloadPath()
    {
        auto options = TestOptions();
        options.computer_name = "";
        options.module = "///";
        auto collector = CreateCollector(options);
        const auto sensor = CreateIntSensor(collector.value, "/contract/path/");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "\"Path\":\"contract/path\"");
    }

    void NativeSlashOnlyComputerNameIsOmittedFromPayloadPath()
    {
        auto options = TestOptions();
        options.computer_name = "///";
        options.module = "";
        auto collector = CreateCollector(options);
        const auto sensor = CreateIntSensor(collector.value, "/contract/path/");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "\"Path\":\"contract/path\"");
    }

    void NativeSlashOnlyModuleAndComputerNameAreOmittedFromPayloadPath()
    {
        auto options = TestOptions();
        options.computer_name = "///";
        options.module = "///";
        auto collector = CreateCollector(options);
        const auto sensor = CreateIntSensor(collector.value, "/contract/path/");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "\"Path\":\"contract/path\"");
    }

    void NativeWhitespaceOnlyPathIsRejected()
    {
        auto collector = CreateCollector();
        auto* sensor = reinterpret_cast<hsm_sensor_t*>(static_cast<uintptr_t>(1));

        Require(
            hsm_collector_create_int_sensor(collector.value, " \t ", &sensor) == HSM_RESULT_INVALID_ARGUMENT,
            "whitespace-only sensor path should fail");
        Require(sensor == nullptr, "rejected whitespace-only sensor create should clear out sensor");
    }

    void NativeInstantStringNullValueIsInvalidAndNotSent()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateStringSensor(collector.value, "native/null/string");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(
            hsm_sensor_add_string(sensor.value, nullptr, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_INVALID_ARGUMENT,
            "null string value should fail");
        Require(hsm_collector_sent_count(collector.value) == 0, "null string value should not be sent");
    }

    void NativeLastValueStringNullDefaultIsInvalid()
    {
        auto collector = CreateCollector();
        auto* sensor = reinterpret_cast<hsm_sensor_t*>(static_cast<uintptr_t>(1));

        Require(
            hsm_collector_create_last_value_string_sensor(collector.value, "native/null/default", nullptr, &sensor) ==
                HSM_RESULT_INVALID_ARGUMENT,
            "null last string default should fail");
        Require(sensor == nullptr, "rejected null last string default should clear out sensor");
    }

    void NativeLastValueStringNullUpdateIsInvalidAndPreservesPrevious()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateLastStringSensor(collector.value, "native/null/update", "default");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_string(sensor.value, "live", HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add string failed");
        Require(
            hsm_sensor_add_string(sensor.value, nullptr, HSM_SENSOR_STATUS_ERROR, "bad") == HSM_RESULT_INVALID_ARGUMENT,
            "null last string update should fail");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "collector stop failed");

        const auto payload = SentJson(collector.value, 0);
        Contains(payload, "\"Value\":\"live\"");
        NotContains(payload, "\"Value\":\"\"");
    }

    void NativeJsonEscapesControlCharsInStringValue()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateStringSensor(collector.value, "native/json/string");
        const auto value = std::string("a") + static_cast<char>(0x01) + "b";

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_string(sensor.value, value.c_str(), HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add string failed");

        Contains(WaitForFirstPayload(collector.value), "\"Value\":\"a\\u0001b\"");
    }

    void NativeJsonEscapesControlCharsInComment()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateIntSensor(collector.value, "native/json/comment");
        const auto comment = std::string("bad") + static_cast<char>(0x1f) + "comment";

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, comment.c_str()) == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "\"Comment\":\"bad\\u001fcomment\"");
    }

    void NativeJsonEscapesControlCharsInPath()
    {
        auto collector = CreateCollector();
        const auto path = std::string("native/json/") + static_cast<char>(0x02) + "/path";
        const auto sensor = CreateIntSensor(collector.value, path.c_str());

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "native/json/\\u0002/path");
    }

    void NativeJsonEscapesControlCharsInOptionsPathPrefix()
    {
        auto options = TestOptions();
        const auto computer_name = std::string("host") + static_cast<char>(0x03);
        const auto module = std::string("module") + static_cast<char>(0x04);
        options.computer_name = computer_name.c_str();
        options.module = module.c_str();
        auto collector = CreateCollector(options);
        const auto sensor = CreateIntSensor(collector.value, "native/json/options");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add int failed");

        Contains(WaitForFirstPayload(collector.value), "host\\u0003/module\\u0004/native/json/options");
    }

    void NativeDoubleNanIsRejectedAndNotSent()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateDoubleSensor(collector.value, "native/double/nan");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(
            hsm_sensor_add_double(sensor.value, std::numeric_limits<double>::quiet_NaN(), HSM_SENSOR_STATUS_OK, "") ==
                HSM_RESULT_INVALID_ARGUMENT,
            "NaN double should fail");
        Require(hsm_collector_sent_count(collector.value) == 0, "NaN double should not be sent");
    }

    void NativeDoublePositiveInfinityIsRejectedAndNotSent()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateDoubleSensor(collector.value, "native/double/positive-infinity");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(
            hsm_sensor_add_double(sensor.value, std::numeric_limits<double>::infinity(), HSM_SENSOR_STATUS_OK, "") ==
                HSM_RESULT_INVALID_ARGUMENT,
            "positive infinity double should fail");
        Require(hsm_collector_sent_count(collector.value) == 0, "positive infinity double should not be sent");
    }

    void NativeDoubleNegativeInfinityIsRejectedAndNotSent()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateDoubleSensor(collector.value, "native/double/negative-infinity");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(
            hsm_sensor_add_double(sensor.value, -std::numeric_limits<double>::infinity(), HSM_SENSOR_STATUS_OK, "") ==
                HSM_RESULT_INVALID_ARGUMENT,
            "negative infinity double should fail");
        Require(hsm_collector_sent_count(collector.value) == 0, "negative infinity double should not be sent");
    }

    void NativeInvalidStatusOnInstantValueIsRejectedAndNotSent()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateIntSensor(collector.value, "native/status/instant");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(
            hsm_sensor_add_int(sensor.value, 1, static_cast<hsm_sensor_status_t>(99), "") == HSM_RESULT_INVALID_ARGUMENT,
            "invalid instant status should fail");
        Require(hsm_collector_sent_count(collector.value) == 0, "invalid instant status should not be sent");
    }

    void NativeInvalidStatusOnLastValuePreservesPreviousSnapshot()
    {
        auto collector = CreateCollector();
        const auto sensor = CreateLastIntSensor(collector.value, "native/status/last", 1);

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 2, HSM_SENSOR_STATUS_OK, "good") == HSM_RESULT_OK, "valid last add failed");
        Require(
            hsm_sensor_add_int(sensor.value, 999, static_cast<hsm_sensor_status_t>(99), "bad") ==
                HSM_RESULT_INVALID_ARGUMENT,
            "invalid last status should fail");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "collector stop failed");

        const auto payload = SentJson(collector.value, 0);
        Contains(payload, "\"Value\":2");
        Contains(payload, "\"Status\":1");
        NotContains(payload, "\"Value\":999");
    }

    const std::map<std::string, std::function<void(const std::string&)>>& Tests()
    {
        static const std::map<std::string, std::function<void(const std::string&)>> tests = {
            { "native_invalid_argument_clears_out_params", [](const std::string&) { NativeInvalidArgumentClearsOutParams(); } },
            { "native_add_after_collector_destroy_is_rejected", [](const std::string&) { NativeAddAfterCollectorDestroyIsRejected(); } },
            { "native_sent_json_failure_reports_fresh_error", [](const std::string&) { NativeSentJsonFailureReportsFreshError(); } },
            { "native_wrapper_sent_json_missing_throws_message", [](const std::string&) { NativeWrapperSentJsonMissingThrowsMessage(); } },
            { "native_create_rejects_null_server_address", [](const std::string&) { NativeCreateRejectsNullServerAddress(); } },
            { "native_create_rejects_blank_server_address", [](const std::string&) { NativeCreateRejectsBlankServerAddress(); } },
            { "native_create_rejects_null_access_key", [](const std::string&) { NativeCreateRejectsNullAccessKey(); } },
            { "native_create_rejects_blank_access_key", [](const std::string&) { NativeCreateRejectsBlankAccessKey(); } },
            { "native_slash_only_module_is_omitted_from_payload_path", [](const std::string&) { NativeSlashOnlyModuleIsOmittedFromPayloadPath(); } },
            { "native_slash_only_computer_name_is_omitted_from_payload_path", [](const std::string&) { NativeSlashOnlyComputerNameIsOmittedFromPayloadPath(); } },
            { "native_slash_only_module_and_computer_name_are_omitted_from_payload_path", [](const std::string&) { NativeSlashOnlyModuleAndComputerNameAreOmittedFromPayloadPath(); } },
            { "native_whitespace_only_path_is_rejected", [](const std::string&) { NativeWhitespaceOnlyPathIsRejected(); } },
            { "native_instant_string_null_value_is_invalid_and_not_sent", [](const std::string&) { NativeInstantStringNullValueIsInvalidAndNotSent(); } },
            { "native_last_value_string_null_default_is_invalid", [](const std::string&) { NativeLastValueStringNullDefaultIsInvalid(); } },
            { "native_last_value_string_null_update_is_invalid_and_preserves_previous", [](const std::string&) { NativeLastValueStringNullUpdateIsInvalidAndPreservesPrevious(); } },
            { "native_json_escapes_control_chars_in_string_value", [](const std::string&) { NativeJsonEscapesControlCharsInStringValue(); } },
            { "native_json_escapes_control_chars_in_comment", [](const std::string&) { NativeJsonEscapesControlCharsInComment(); } },
            { "native_json_escapes_control_chars_in_path", [](const std::string&) { NativeJsonEscapesControlCharsInPath(); } },
            { "native_json_escapes_control_chars_in_options_path_prefix", [](const std::string&) { NativeJsonEscapesControlCharsInOptionsPathPrefix(); } },
            { "native_double_nan_is_rejected_and_not_sent", [](const std::string&) { NativeDoubleNanIsRejectedAndNotSent(); } },
            { "native_double_positive_infinity_is_rejected_and_not_sent", [](const std::string&) { NativeDoublePositiveInfinityIsRejectedAndNotSent(); } },
            { "native_double_negative_infinity_is_rejected_and_not_sent", [](const std::string&) { NativeDoubleNegativeInfinityIsRejectedAndNotSent(); } },
            { "native_invalid_status_on_instant_value_is_rejected_and_not_sent", [](const std::string&) { NativeInvalidStatusOnInstantValueIsRejectedAndNotSent(); } },
            { "native_invalid_status_on_last_value_preserves_previous_snapshot", [](const std::string&) { NativeInvalidStatusOnLastValuePreservesPreviousSnapshot(); } },
            { "conformance_instant_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_lifecycle_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_stress_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_stress_mixed_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_value_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_cardinality_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_instant_mixed_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_last_value_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_enum_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_bar_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_bar_double_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_bar_partial_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_bar_rollover_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_queue_overflow_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_sender_retry_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_flush_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_rate_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_function_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_file_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_number_format_contract", [](const std::string& path) { RunConformanceContract(path); } },
        };

        return tests;
    }
}

int main(int argc, char** argv)
{
    try
    {
        const auto& tests = Tests();

        if (argc > 1)
        {
            const auto test = tests.find(argv[1]);
            if (test == tests.end())
                throw std::runtime_error(std::string("Unknown test: ") + argv[1]);

            const auto argument = argc > 2 ? std::string{ argv[2] } : std::string{};
            test->second(argument);
            return 0;
        }

        throw std::runtime_error("Pass a conformance test name and fixture path.");
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }

    return 0;
}
