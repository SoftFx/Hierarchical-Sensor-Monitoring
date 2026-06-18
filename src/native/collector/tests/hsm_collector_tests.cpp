#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"
#include "../src/hsm_http_endpoints.hpp"
#include "../src/hsm_http_retry.hpp"
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

#if defined(HSM_COLLECTOR_HTTP)
#include "../src/hsm_http_transport.hpp"
#include "http_capture_server.hpp"
#endif

// Test-only seam (defined in hsm_collector.cpp, deliberately not in the public header): drive
// the scheduler's injectable clock from the native unit tests (issue #1095 §13).
extern "C" void hsm_collector_test_install_manual_clock(hsm_collector_t* collector, int64_t base_ms);
extern "C" void hsm_collector_test_advance_clock_ms(hsm_collector_t* collector, int64_t delta_ms);
extern "C" void hsm_collector_test_log_error(hsm_collector_t* collector, const char* message);
#if defined(HSM_COLLECTOR_HTTP)
// Live-path seam (#1097): swap the recording sender for the libcurl transport before Start.
extern "C" void hsm_collector_test_install_http_sender(hsm_collector_t* collector);
#endif
// Real-wire serializers (#1096): exercised directly from the unit tests.
extern "C" const char* hsm_collector_test_iso_from_unix_ms(int64_t unix_ms);
extern "C" const char* hsm_collector_test_timespan_c_format(int64_t ticks);
extern "C" const char* hsm_collector_test_wire_value_json(
    int32_t type,
    const char* value_json,
    const char* comment,
    int comment_is_null,
    int32_t status,
    int64_t time_ms,
    const char* path);
extern "C" const char* hsm_collector_test_wire_bar_json(
    int is_int,
    double min,
    double max,
    double total_sum,
    double first,
    double last,
    int32_t count,
    int precision,
    int64_t open_ms,
    int64_t close_ms,
    int64_t time_ms,
    const char* path);
extern "C" const char* hsm_collector_test_wire_file_json(
    const char* extension,
    const char* name,
    const char* content,
    const char* comment,
    int comment_is_null,
    int32_t status,
    int64_t time_ms,
    const char* path);
extern "C" const char* hsm_collector_test_wire_registration_json(
    int32_t type,
    int64_t ttl_ms,
    int32_t unit,
    int has_description,
    const char* description,
    int has_enum,
    int32_t enum_key,
    const char* enum_value,
    const char* enum_description,
    int32_t enum_color,
    const char* path);
extern "C" const char* hsm_sensor_test_wire_registration_json(hsm_sensor_t* sensor);
extern "C" const char* hsm_alert_test_wire_json(hsm_alert_t* alert);
extern "C" const char* hsm_collector_test_merge_registration_json(
    int proto_is_computer,
    int64_t proto_ttl_ms,
    const char* proto_description,
    int64_t custom_ttl_ms,
    const char* custom_description,
    int custom_has_description,
    const char* path);

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
        // The managed package/period defaults dispatch every 15 s; the corpus needs fast
        // dispatch, so the harness sets the small test values explicitly (matching the C#
        // CollectorConformanceTests harness). create_collector_with_limits overrides these.
        options.max_values_in_package = 50;
        options.package_collect_period_ms = 20;
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

    bool WaitForRegistrationCountEquals(hsm_collector_t* collector, size_t expected, int timeout_ms = 2000)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);

        while (std::chrono::steady_clock::now() < deadline)
        {
            if (hsm_collector_registration_count(collector) == expected)
                return true;

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        return hsm_collector_registration_count(collector) == expected;
    }

    std::string RegistrationJson(hsm_collector_t* collector, size_t index)
    {
        const char* json = nullptr;
        Require(
            hsm_collector_get_registration_json(collector, index, &json) == HSM_RESULT_OK && json != nullptr,
            "registration json was not found");
        return json;
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

    std::vector<std::string> SplitBy(const std::string& text, char separator)
    {
        std::vector<std::string> parts;
        std::string part;
        std::istringstream input(text);

        while (std::getline(input, part, separator))
            parts.push_back(part);

        return parts;
    }

    std::vector<std::string> SplitLine(const std::string& line)
    {
        return SplitBy(line, '|');
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
        // the raw user_data pointer). MUST be destroyed AFTER the collector: the scheduler keeps
        // invoking the function callback (which reads this user_data) until the collector is
        // destroyed, so the collector is declared LAST here — members destruct in reverse
        // declaration order, so the collector (and its scheduler-worker join) tears down first,
        // before these constants are freed. (TSan caught the reverse order as a use-after-free.)
        std::vector<std::unique_ptr<int32_t>> function_constants;

        // Alert builder mirror (numeric enum args). The handles are owned by the collector, so
        // these are non-owning raw pointers valid until the collector is destroyed.
        hsm_alert_t* current_alert = nullptr;
        std::vector<hsm_alert_t*> pending_alerts;

        CollectorHandle collector;
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

        if (action == "create_int_sensor_with_options")
        {
            Require(step.size() >= 5, "create_int_sensor_with_options requires path, ttl_ms, unit, description");
            const auto path = ExpandTextToken(step[1]);
            const auto description = ExpandTextToken(step[4]);

            SensorHandle sensor;
            Require(
                hsm_collector_create_int_sensor_with_options(
                    state.collector.value, path.c_str(),
                    static_cast<int64_t>(std::stoll(step[2])), ToInt(step[3]),
                    description.c_str(), &sensor.value) == HSM_RESULT_OK,
                "sensor create with options failed");

            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_enum_sensor_with_options")
        {
            Require(step.size() >= 4, "create_enum_sensor_with_options requires path, description, options");
            const auto path = ExpandTextToken(step[1]);
            const auto description = ExpandTextToken(step[2]);

            // "key:value:color:description;..." — keep the string storage alive across the call.
            std::vector<std::vector<std::string>> parsed;
            for (const auto& part : SplitBy(step[3], ';'))
            {
                auto fields = SplitBy(part, ':');
                Require(fields.size() == 4, "enum option must be key:value:color:description");
                parsed.push_back(std::move(fields));
            }

            std::vector<hsm_enum_option_t> options;
            options.reserve(parsed.size());
            for (const auto& fields : parsed)
                options.push_back(hsm_enum_option_t{
                    ToInt(fields[0]), fields[1].c_str(), ToInt(fields[2]), fields[3].c_str() });

            SensorHandle sensor;
            Require(
                hsm_collector_create_enum_sensor_with_options(
                    state.collector.value, path.c_str(), description.c_str(),
                    options.data(), options.size(), &sensor.value) == HSM_RESULT_OK,
                "enum sensor create with options failed");

            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_int_sensor_full_options")
        {
            Require(step.size() >= 13, "create_int_sensor_full_options requires 12 args");
            const auto path = ExpandTextToken(step[1]);
            const auto description = ExpandTextToken(step[12]);

            hsm_sensor_options_t options{};
            options.ttl_ms = static_cast<int64_t>(std::stoll(step[2]));
            options.unit = ToInt(step[3]);
            options.description = description.c_str();
            options.keep_history_ms = static_cast<int64_t>(std::stoll(step[4]));
            options.self_destroy_ms = static_cast<int64_t>(std::stoll(step[5]));
            options.display_unit = -1; // instant sensors carry no DisplayUnit
            options.statistics = ToInt(step[6]);
            options.is_singleton = ToInt(step[7]);
            options.aggregate_data = ToInt(step[8]);
            options.enable_grafana = ToInt(step[9]);
            options.is_computer_sensor = ToBool(step[10]);
            options.sensor_location = ToInt(step[11]);

            SensorHandle sensor;
            Require(
                hsm_collector_create_sensor_with_options(state.collector.value, path.c_str(), HSM_SENSOR_TYPE_INT, &options, &sensor.value) == HSM_RESULT_OK,
                "create_int_sensor_full_options failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_service_commands_sensor")
        {
            SensorHandle sensor;
            Require(hsm_collector_create_service_commands_sensor(state.collector.value, &sensor.value) == HSM_RESULT_OK, "service-commands sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "service_send_custom" || action == "service_send_restart" || action == "service_send_start" ||
            action == "service_send_stop" || action == "service_send_update" || action == "service_send_update_version")
        {
            Require(step.size() >= 3, "service_send_* requires sensor index and initiator");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            auto* sensor = state.sensors[sensor_index].value;

            hsm_result_t result = HSM_RESULT_OK;
            if (action == "service_send_custom")
            {
                const auto command = ExpandTextToken(step[2]);
                const auto initiator = ExpandTextToken(step[3]);
                result = hsm_service_commands_send_custom(sensor, command.c_str(), initiator.c_str());
            }
            else if (action == "service_send_update_version")
            {
                const auto initiator = ExpandTextToken(step[2]);
                const auto new_version = ExpandTextToken(step[3]);
                const bool has_old = step.size() >= 5;
                const auto old_version = has_old ? ExpandTextToken(step[4]) : std::string{};
                result = hsm_service_commands_send_update_version(sensor, initiator.c_str(), new_version.c_str(), has_old ? old_version.c_str() : nullptr);
            }
            else
            {
                const auto initiator = ExpandTextToken(step[2]);
                if (action == "service_send_restart")
                    result = hsm_service_commands_send_restart(sensor, initiator.c_str());
                else if (action == "service_send_start")
                    result = hsm_service_commands_send_start(sensor, initiator.c_str());
                else if (action == "service_send_stop")
                    result = hsm_service_commands_send_stop(sensor, initiator.c_str());
                else
                    result = hsm_service_commands_send_update(sensor, initiator.c_str());
            }
            Require(result == HSM_RESULT_OK, "service_send_* failed");
            return;
        }

        if (action == "create_timespan_sensor")
        {
            Require(step.size() >= 2, "create_timespan_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(hsm_collector_create_timespan_sensor(state.collector.value, path.c_str(), &sensor.value) == HSM_RESULT_OK, "timespan sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "create_version_sensor")
        {
            Require(step.size() >= 2, "create_version_sensor requires path");
            const auto path = ExpandTextToken(step[1]);
            SensorHandle sensor;
            Require(hsm_collector_create_version_sensor(state.collector.value, path.c_str(), &sensor.value) == HSM_RESULT_OK, "version sensor create failed");
            state.sensors.push_back(std::move(sensor));
            return;
        }

        if (action == "add_timespan")
        {
            Require(step.size() >= 5, "add_timespan requires sensor index, ticks, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto comment = ExpandTextToken(step[4]);
            Require(
                hsm_sensor_add_timespan(state.sensors[sensor_index].value, static_cast<int64_t>(std::stoll(step[2])), ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_timespan failed");
            return;
        }

        if (action == "add_version")
        {
            Require(step.size() >= 5, "add_version requires sensor index, version, status, and comment");
            const auto sensor_index = static_cast<size_t>(ToInt(step[1]));
            Require(sensor_index < state.sensors.size(), "sensor index out of range");
            const auto comment = ExpandTextToken(step[4]);

            // "major.minor[.build[.revision]]" -> components, absent ones become -1.
            const auto parts = SplitBy(step[2], '.');
            Require(parts.size() >= 2, "version requires at least major.minor");
            const int32_t major = ToInt(parts[0]);
            const int32_t minor = ToInt(parts[1]);
            const int32_t build = parts.size() >= 3 ? ToInt(parts[2]) : -1;
            const int32_t revision = parts.size() >= 4 ? ToInt(parts[3]) : -1;
            Require(
                hsm_sensor_add_version(state.sensors[sensor_index].value, major, minor, build, revision, ToStatus(step[3]), comment.c_str()) == HSM_RESULT_OK,
                "add_version failed");
            return;
        }

        // ---- Alert builder verbs (numeric enum args; see tests/conformance/README.md) ----
        if (action == "alert_new")
        {
            Require(step.size() >= 2, "alert_new requires kind");
            hsm_alert_kind_t kind = HSM_ALERT_KIND_INSTANT;
            if (step[1] == "bar")
                kind = HSM_ALERT_KIND_BAR;
            else if (step[1] == "ttl")
                kind = HSM_ALERT_KIND_TTL;
            Require(hsm_collector_create_alert(state.collector.value, kind, &state.current_alert) == HSM_RESULT_OK, "alert_new failed");
            return;
        }

        if (action == "alert_condition")
        {
            Require(step.size() >= 5, "alert_condition requires combination, property, operation, target_type[, value]");
            const bool has_value = step.size() >= 6;
            const auto value = has_value ? ExpandTextToken(step[5]) : std::string{};
            Require(
                hsm_alert_add_condition(
                    state.current_alert,
                    static_cast<hsm_alert_combination_t>(ToInt(step[1])),
                    static_cast<hsm_alert_property_t>(ToInt(step[2])),
                    static_cast<hsm_alert_operation_t>(ToInt(step[3])),
                    static_cast<hsm_alert_target_type_t>(ToInt(step[4])),
                    has_value ? value.c_str() : nullptr) == HSM_RESULT_OK,
                "alert_condition failed");
            return;
        }

        if (action == "alert_notification")
        {
            Require(step.size() >= 3, "alert_notification requires template and destination");
            const auto notification_template = ExpandTextToken(step[1]);
            Require(
                hsm_alert_set_notification(state.current_alert, notification_template.c_str(), static_cast<hsm_alert_destination_mode_t>(ToInt(step[2]))) == HSM_RESULT_OK,
                "alert_notification failed");
            return;
        }

        if (action == "alert_icon")
        {
            Require(step.size() >= 2, "alert_icon requires icon");
            Require(hsm_alert_set_icon(state.current_alert, static_cast<hsm_alert_icon_t>(ToInt(step[1]))) == HSM_RESULT_OK, "alert_icon failed");
            return;
        }

        if (action == "alert_sensor_error")
        {
            Require(hsm_alert_set_sensor_error(state.current_alert) == HSM_RESULT_OK, "alert_sensor_error failed");
            return;
        }

        if (action == "alert_confirmation")
        {
            Require(step.size() >= 2, "alert_confirmation requires period ms");
            Require(hsm_alert_set_confirmation_period(state.current_alert, static_cast<int64_t>(std::stoll(step[1]))) == HSM_RESULT_OK, "alert_confirmation failed");
            return;
        }

        if (action == "alert_inactivity")
        {
            Require(step.size() >= 2, "alert_inactivity requires period ms");
            Require(hsm_alert_set_inactivity_period(state.current_alert, static_cast<int64_t>(std::stoll(step[1]))) == HSM_RESULT_OK, "alert_inactivity failed");
            return;
        }

        if (action == "alert_disabled")
        {
            Require(hsm_alert_set_disabled(state.current_alert, true) == HSM_RESULT_OK, "alert_disabled failed");
            return;
        }

        if (action == "alert_stage")
        {
            Require(state.current_alert != nullptr, "alert_stage with no current alert");
            state.pending_alerts.push_back(state.current_alert);
            state.current_alert = nullptr;
            return;
        }

        if (action == "create_int_sensor_with_alerts")
        {
            Require(step.size() >= 5, "create_int_sensor_with_alerts requires path, ttl_ms, unit, description");
            const auto path = ExpandTextToken(step[1]);
            const auto description = ExpandTextToken(step[4]);

            SensorHandle sensor;
            Require(
                hsm_collector_create_int_sensor_with_options(
                    state.collector.value, path.c_str(),
                    static_cast<int64_t>(std::stoll(step[2])), ToInt(step[3]),
                    description.c_str(), &sensor.value) == HSM_RESULT_OK,
                "alert sensor create failed");

            for (auto* alert : state.pending_alerts)
                Require(hsm_sensor_attach_alert(sensor.value, alert) == HSM_RESULT_OK, "attach pending alert failed");
            state.pending_alerts.clear();

            state.sensors.push_back(std::move(sensor));
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
                workers.emplace_back([&, worker]() {
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

        const auto add_mixed_instant_value = [&](size_t set_index, int value, hsm_sensor_status_t status, const std::string& comment) {
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
                workers.emplace_back([&, worker]() {
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

            const auto expect_rejected = [](hsm_result_t result, hsm_sensor_t* sensor) {
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
                workers.emplace_back([&, worker]() {
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
                 ", got " + std::to_string(hsm_collector_sent_count(state.collector.value)))
                    .c_str());
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

        if (action == "dump_payloads_to")
        {
            // Differential-fuzzer support: canonical payload texts, one per line, LF endings
            // (binary mode defeats Windows CRLF translation) — byte-comparable with the C# dump.
            Require(step.size() >= 2, "dump_payloads_to requires a file path");

            std::ofstream output(step[1], std::ios::binary | std::ios::trunc);
            Require(static_cast<bool>(output), "cannot open payload dump file");

            const auto count = hsm_collector_sent_count(state.collector.value);
            for (size_t i = 0; i < count; ++i)
            {
                // The spike's instant wire json carries a UnixTimeMs field the canonical C#
                // text does not have — strip it so the dumps are byte-comparable.
                auto line = SentJson(state.collector.value, i);
                const std::string time_key = ",\"UnixTimeMs\":";
                const auto time_field = line.find(time_key);
                if (time_field != std::string::npos)
                {
                    auto end = time_field + time_key.size();
                    while (end < line.size() && (line[end] == '-' || (line[end] >= '0' && line[end] <= '9')))
                        ++end;
                    line.erase(time_field, end - time_field);
                }

                output << line << '\n';
            }
            return;
        }

        if (action == "expect_registration_count")
        {
            Require(step.size() >= 2, "expect_registration_count requires count");
            const auto expected = static_cast<size_t>(ToInt(step[1]));
            const auto timeout_ms = step.size() >= 3 ? ToInt(step[2]) * 1000 : 2000;

            Require(
                WaitForRegistrationCountEquals(state.collector.value, expected, timeout_ms),
                ("registration count did not match: expected " + step[1] +
                 ", got " + std::to_string(hsm_collector_registration_count(state.collector.value)))
                    .c_str());
            return;
        }

        if (action == "expect_registration_contains")
        {
            Require(step.size() >= 3, "expect_registration_contains requires index and substring");
            Contains(RegistrationJson(state.collector.value, static_cast<size_t>(ToInt(step[1]))), step[2]);
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

            const auto count_type = [&](const std::string& type) {
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
                workers.emplace_back([&, worker]() {
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
                                        std::chrono::steady_clock::now() - started)
                                        .count();

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
                { "type", "Type" },
                { "min", "Min" },
                { "max", "Max" },
                { "mean", "Mean" },
                { "first", "First" },
                { "last", "Last" },
                { "count", "Count" },
                { "status", "Status" },
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

    // Meta-suite ("test the tests", #1094): the fixture carries a deliberately wrong
    // expectation, and a correct driver MUST fail it. The contract run is wrapped in-process
    // so a crash still fails the meta test — a segfault is not "detection".
    void RunConformanceContractExpectFailure(const std::string& path)
    {
        // Drivers abort a fixture on the first failing step, so a second case in a
        // must-fail fixture would never be proven — one mutation per file.
        Require(ReadConformanceFile(path).size() == 1, "meta fixture must contain exactly one case");

        try
        {
            RunConformanceContract(path);
        }
        catch (const std::exception& ex)
        {
            std::cout << "must-fail fixture failed as required: " << ex.what() << '\n';
            return;
        }

        throw std::runtime_error("Must-fail fixture unexpectedly passed: " + path);
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
        hsm::collector::CollectorOptions options;
        options.access_key = "test-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "native-wrapper";
        options.computer_name = "native-host";

        hsm::collector::Collector collector(options);

        try
        {
            (void)collector.SentJson(0);
        }
        catch (const hsm::collector::Error& ex)
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

    // --- #1095 native core: ABI version, lifecycle state machine, dispose, listeners,
    // TestConnection, MaxSensors cap, options validation, log sink. ---

    void NativeVersionMatchesMacro()
    {
        Require(hsm_collector_version() == HSM_COLLECTOR_VERSION, "version() must match the header macro");
        Require(HSM_COLLECTOR_VERSION_MAJOR == 0 && HSM_COLLECTOR_VERSION_MINOR >= 2, "unexpected ABI version");
    }

    void NativeStatusTracksLifecycle()
    {
        auto collector = CreateCollector();
        Require(hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_STOPPED, "fresh collector should be Stopped");
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        Require(hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_RUNNING, "started collector should be Running");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
        Require(hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_STOPPED, "stopped collector should be Stopped");
    }

    void NativeDisposeIsTerminalAndIdempotent()
    {
        auto collector = CreateCollector();
        hsm_collector_dispose(collector.value);
        Require(hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_DISPOSED, "disposed collector should be Disposed");
        hsm_collector_dispose(collector.value);
        Require(hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_DISPOSED, "second dispose should be a no-op");
        Require(hsm_collector_start(collector.value) == HSM_RESULT_INVALID_STATE, "start after dispose must be rejected");

        hsm_sensor_t* sensor = nullptr;
        Require(
            hsm_collector_create_int_sensor(collector.value, "after/dispose", &sensor) == HSM_RESULT_INVALID_STATE,
            "registration after dispose must be rejected");
        Require(sensor == nullptr, "rejected registration must return a null handle");
    }

    void NativeDisposeFromRunningStops()
    {
        auto collector = CreateCollector();
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        hsm_collector_dispose(collector.value);
        Require(
            hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_DISPOSED,
            "dispose from running should reach Disposed");
    }

    void NativeLifecycleListenerReceivesTransitions()
    {
        std::vector<hsm_collector_status_t> seen;
        auto collector = CreateCollector();
        Require(
            hsm_collector_add_lifecycle_listener(
                collector.value,
                [](hsm_collector_status_t status, void* user_data) {
                    static_cast<std::vector<hsm_collector_status_t>*>(user_data)->push_back(status);
                },
                &seen) == HSM_RESULT_OK,
            "add listener failed");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");

        const std::vector<hsm_collector_status_t> expected = {
            HSM_COLLECTOR_STATUS_STARTING,
            HSM_COLLECTOR_STATUS_RUNNING,
            HSM_COLLECTOR_STATUS_STOPPING,
            HSM_COLLECTOR_STATUS_STOPPED
        };
        Require(seen == expected, "listener should observe Starting,Running,Stopping,Stopped in order");
    }

    void NativeLifecycleListenerExceptionIsIsolated()
    {
        auto collector = CreateCollector();
        Require(
            hsm_collector_add_lifecycle_listener(
                collector.value,
                [](hsm_collector_status_t, void*) { throw std::runtime_error("listener boom"); },
                nullptr) == HSM_RESULT_OK,
            "add listener failed");

        // A throwing listener must neither break the transition nor escape the ABI.
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start must survive a throwing listener");
        Require(
            hsm_collector_status(collector.value) == HSM_COLLECTOR_STATUS_RUNNING,
            "collector must still be Running after a throwing listener");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop must survive a throwing listener");
    }

    void NativeTestConnectionReportsReachable()
    {
        auto collector = CreateCollector();
        Require(hsm_collector_test_connection(collector.value) == HSM_RESULT_OK, "test connection should be OK when stopped");
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        Require(hsm_collector_test_connection(collector.value) == HSM_RESULT_OK, "test connection should be OK while running");
        hsm_collector_dispose(collector.value);
        Require(
            hsm_collector_test_connection(collector.value) == HSM_RESULT_INVALID_STATE,
            "test connection on a disposed collector should report invalid state");
    }

    void NativeMaxSensorsCapRejectsBeyondLimit()
    {
        auto options = TestOptions();
        options.max_sensors = 2;
        auto collector = CreateCollector(options);

        hsm_sensor_t* a = nullptr;
        hsm_sensor_t* b = nullptr;
        hsm_sensor_t* c = nullptr;
        Require(hsm_collector_create_int_sensor(collector.value, "cap/a", &a) == HSM_RESULT_OK, "first sensor should be accepted");
        Require(hsm_collector_create_int_sensor(collector.value, "cap/b", &b) == HSM_RESULT_OK, "second sensor should be accepted");
        Require(
            hsm_collector_create_int_sensor(collector.value, "cap/c", &c) == HSM_RESULT_LIMIT_EXCEEDED,
            "third sensor should hit the MaxSensors cap");
        Require(c == nullptr, "rejected sensor must return a null handle");

        // A duplicate path returns the existing handle and does not count against the cap.
        hsm_sensor_t* a_again = nullptr;
        Require(
            hsm_collector_create_int_sensor(collector.value, "cap/a", &a_again) == HSM_RESULT_OK,
            "duplicate path must stay idempotent under the cap");

        hsm_sensor_release(a);
        hsm_sensor_release(b);
        hsm_sensor_release(a_again);
    }

    void NativeCreateRejectsNegativeOptionFields()
    {
        {
            auto options = TestOptions();
            options.max_queue_size = -1;
            ExpectCollectorCreateRejected(options);
        }
        {
            auto options = TestOptions();
            options.exception_deduplicator_window_ms = -5;
            ExpectCollectorCreateRejected(options);
        }
        {
            auto options = TestOptions();
            options.max_sensors = -10;
            ExpectCollectorCreateRejected(options);
        }
    }

    void NativeLoggerSinkCanBeSetAndCleared()
    {
        int calls = 0;
        auto collector = CreateCollector();
        Require(
            hsm_collector_set_logger(
                collector.value,
                [](hsm_log_level_t, const char*, void* user_data) { ++*static_cast<int*>(user_data); },
                &calls) == HSM_RESULT_OK,
            "set logger failed");
        Require(hsm_collector_set_logger(collector.value, nullptr, nullptr) == HSM_RESULT_OK, "clearing logger failed");
        // Lifecycle still works with a logger attached and then detached.
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
    }

    void NativeSchedulerClockSeamDrivesPeriodicPosts()
    {
        auto collector = CreateCollector();
        // Install a virtual clock BEFORE start so both the scheduler wait and the sensor
        // due-checks read it (issue #1095 §13 injectable clock seam).
        hsm_collector_test_install_manual_clock(collector.value, 1000000);

        hsm_sensor_t* sensor = nullptr;
        Require(
            hsm_collector_create_function_int_sensor(
                collector.value, "seam/func", 100, [](void*) -> int32_t { return 7; }, nullptr, &sensor) == HSM_RESULT_OK,
            "create function sensor failed");
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");

        // The first post fires immediately on start (next_post == clock base).
        Require(WaitForSentCountAtLeast(collector.value, 1, 2000), "first periodic post should fire immediately on start");
        const auto first = hsm_collector_sent_count(collector.value);

        // Real time passing must NOT advance the virtual clock: no further posts appear.
        std::this_thread::sleep_for(std::chrono::milliseconds(150));
        Require(hsm_collector_sent_count(collector.value) == first, "frozen virtual clock: real time must not drive posts");

        // Advancing virtual time past one period makes exactly one more post due.
        hsm_collector_test_advance_clock_ms(collector.value, 150);
        Require(
            WaitForSentCountAtLeast(collector.value, first + 1, 2000),
            "advancing the virtual clock should drive the next post");

        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
        hsm_sensor_release(sensor);
    }

    void NativeSchedulerOnErrorIsolatesThrowingCallback()
    {
        auto collector = CreateCollector();
        // A function sensor whose callback throws must not kill the scheduler loop; a second
        // healthy function sensor must keep posting (onError contract, issue #1095 §13).
        hsm_sensor_t* boom = nullptr;
        hsm_sensor_t* ok = nullptr;
        Require(
            hsm_collector_create_function_int_sensor(
                collector.value, "sched/boom", 20, [](void*) -> int32_t { throw std::runtime_error("boom"); }, nullptr,
                &boom) == HSM_RESULT_OK,
            "create throwing sensor failed");
        Require(
            hsm_collector_create_function_int_sensor(
                collector.value, "sched/ok", 20, [](void*) -> int32_t { return 1; }, nullptr, &ok) == HSM_RESULT_OK,
            "create healthy sensor failed");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");
        Require(
            WaitForSentCountAtLeast(collector.value, 3, 3000),
            "healthy sensor must keep posting despite a throwing callback on every tick");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
        hsm_sensor_release(boom);
        hsm_sensor_release(ok);
    }

    void NativeLoggerDeduplicatesRepeatedErrorsWithinWindow()
    {
        std::vector<std::string> logs;
        auto options = TestOptions();
        options.exception_deduplicator_window_ms = 3600000; // 1 h: nothing expires during the test
        auto collector = CreateCollector(options);
        hsm_collector_set_logger(
            collector.value,
            [](hsm_log_level_t, const char* message, void* ud) {
                static_cast<std::vector<std::string>*>(ud)->emplace_back(message);
            },
            &logs);

        for (int i = 0; i < 5; ++i)
            hsm_collector_test_log_error(collector.value, "disk full");

        Require(logs.size() == 1, "repeated errors within the window must collapse to a single log line");
        Require(logs[0] == "disk full", "the first occurrence is logged verbatim");
    }

    void NativeLoggerZeroWindowLogsEveryError()
    {
        std::vector<std::string> logs;
        auto options = TestOptions();
        options.exception_deduplicator_window_ms = 0; // log immediately, no dedup (managed zero-window contract)
        auto collector = CreateCollector(options);
        hsm_collector_set_logger(
            collector.value,
            [](hsm_log_level_t, const char* message, void* ud) {
                static_cast<std::vector<std::string>*>(ud)->emplace_back(message);
            },
            &logs);

        for (int i = 0; i < 3; ++i)
            hsm_collector_test_log_error(collector.value, "blip");

        Require(logs.size() == 3, "a zero window logs every error immediately with no deduplication");
    }

    void NativeLifecycleListenerCanRegisterAnotherListener()
    {
        auto collector = CreateCollector();
        // A listener that registers another listener from within its callback must not deadlock
        // on the lifecycle lock, nor corrupt the listener vector mid-iteration (snapshot-then-invoke).
        hsm_collector_add_lifecycle_listener(
            collector.value,
            [](hsm_collector_status_t, void* ud) {
                hsm_collector_add_lifecycle_listener(
                    static_cast<hsm_collector_t*>(ud), [](hsm_collector_status_t, void*) {}, nullptr);
            },
            collector.value);

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start must not deadlock when a listener adds a listener");
        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop must not deadlock when a listener adds a listener");
    }

    // --- #1096 real-wire serialization (§15): byte-identical to net8 System.Text.Json. ---

    void NativeWireIsoFromUnixMsMatchesNet()
    {
        Require(std::string(hsm_collector_test_iso_from_unix_ms(0)) == "1970-01-01T00:00:00Z", "epoch ISO");
        Require(std::string(hsm_collector_test_iso_from_unix_ms(1000)) == "1970-01-01T00:00:01Z", "whole-second ISO drops the fraction");
        Require(std::string(hsm_collector_test_iso_from_unix_ms(1500)) == "1970-01-01T00:00:01.5Z", "500ms trims to .5");
        Require(std::string(hsm_collector_test_iso_from_unix_ms(123)) == "1970-01-01T00:00:00.123Z", "123ms keeps three digits");
        Require(std::string(hsm_collector_test_iso_from_unix_ms(50)) == "1970-01-01T00:00:00.05Z", "50ms trims to .05");

        // Pre-epoch (negative) input via the manual-clock seam: floored division must keep millis in
        // [0,999] and floor the second, not truncate toward zero. -500ms == 1969-12-31T23:59:59.5Z.
        Require(std::string(hsm_collector_test_iso_from_unix_ms(-500)) == "1969-12-31T23:59:59.5Z", "negative ms floors correctly");
        Require(std::string(hsm_collector_test_iso_from_unix_ms(-1000)) == "1969-12-31T23:59:59Z", "negative whole second");
    }

    void NativeWireValueJsonMatchesNetByteLayout()
    {
        // Matches the observed net8 byte layout: Type, Value, Comment, Time, Status, Key, Path;
        // Comment null emitted as `null`; Key always null; Time ISO-8601 Z.
        Require(
            std::string(hsm_collector_test_wire_value_json(
                1, "42", "", 1 /*comment null*/, 1 /*Ok*/, 0, "p/int")) == "{\"Type\":1,\"Value\":42,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/int\"}",
            "int wire value layout");

        Require(
            std::string(hsm_collector_test_wire_value_json(
                3, "\"hi\"", "note", 0 /*comment present*/, 2 /*Warning*/, 1500, "p/s")) == "{\"Type\":3,\"Value\":\"hi\",\"Comment\":\"note\",\"Time\":\"1970-01-01T00:00:01.5Z\",\"Status\":2,\"Key\":null,\"Path\":\"p/s\"}",
            "string wire value layout with comment + fractional time");

        // bool (Type 0) and double (Type 2) — the most drift-prone value DTOs, cross-locked by
        // WireFormatGoldenLockTests.Double_bool_and_doublebar_dtos_match_the_native_golden_bytes.
        Require(
            std::string(hsm_collector_test_wire_value_json(
                0, "true", "", 1, 1, 0, "p/b")) == "{\"Type\":0,\"Value\":true,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/b\"}",
            "bool wire value layout");
        Require(
            std::string(hsm_collector_test_wire_value_json(
                2, "0.1", "", 1, 1, 0, "p/d")) == "{\"Type\":2,\"Value\":0.1,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/d\"}",
            "double wire value layout");

        // String escaping must equal System.Text.Json's default encoder byte-for-byte: < > & ' + "
        // and non-ASCII as \uXXXX (uppercase), \\ for backslash, \t for tab — the quote is ",
        // NOT \". The tricky string sits in Comment (which BuildWireValueJson escapes via EscapeJson,
        // the same function used on string Values). Mirrors WireFormatGoldenLockTests escaping case.
        // Input bytes: a < b > c & d ' e + f " g \ h U+00E9(C3 A9) U+2603(E2 98 83) <tab> j
        Require(
            std::string(hsm_collector_test_wire_value_json(
                3, "\"hi\"", "a<b>c&d'e+f\"g\\h\xC3\xA9\xE2\x98\x83\tj", 0, 1, 0, "p/esc")) ==
                "{\"Type\":3,\"Value\":\"hi\",\"Comment\":\"a\\u003Cb\\u003Ec\\u0026d\\u0027e\\u002Bf\\u0022g\\\\h\\u00E9\\u2603\\tj\","
                "\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/esc\"}",
            "string escaping matches the System.Text.Json default encoder");
    }

    void NativeWireBarJsonMatchesNetByteLayout()
    {
        // int bar: min1 max5 sum15 count5 -> mean nearbyint(3); open epoch, close +2s.
        Require(
            std::string(hsm_collector_test_wire_bar_json(
                1, 1, 5, 15, 1, 5, 5, 2, 0, 2000, 0, "p/ib")) == "{\"Type\":4,\"Min\":1,\"Max\":5,\"Mean\":3,\"FirstValue\":1,\"LastValue\":5,\"Percentiles\":null,"
                                                                 "\"OpenTime\":\"1970-01-01T00:00:00Z\",\"CloseTime\":\"1970-01-01T00:00:02Z\",\"Count\":5,"
                                                                 "\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/ib\"}",
            "int bar wire layout");

        // int-bar Mean rounding must match C# `(int)Math.Round(_totalSum / Count)`
        // (MonitoringBar.cs:128), which is round-HALF-TO-EVEN. nearbyint in the default FE mode is
        // also half-to-even, so 2.5 -> 2 and 3.5 -> 4 on BOTH sides. (Round-away-from-zero would
        // give 3 and 4 and break parity.) Pins the half-way cases the all-integer case can't.
        Require(
            std::string(hsm_collector_test_wire_bar_json(1, 2, 3, 5, 2, 3, 2, 2, 0, 2000, 0, "p/ib")).find("\"Mean\":2,") != std::string::npos,
            "int bar mean 2.5 rounds half-to-even -> 2");
        Require(
            std::string(hsm_collector_test_wire_bar_json(1, 3, 4, 7, 3, 4, 2, 2, 0, 2000, 0, "p/ib")).find("\"Mean\":4,") != std::string::npos,
            "int bar mean 3.5 rounds half-to-even -> 4");

        // double bar (Type 5): sum13/count4 -> mean 3.25; min/max/first/last carry one decimal.
        // Cross-locked by WireFormatGoldenLockTests double-bar case.
        Require(
            std::string(hsm_collector_test_wire_bar_json(
                0, 1.5, 5.5, 13.0, 1.5, 5.5, 4, 2, 0, 2000, 0, "p/db")) == "{\"Type\":5,\"Min\":1.5,\"Max\":5.5,\"Mean\":3.25,\"FirstValue\":1.5,\"LastValue\":5.5,\"Percentiles\":null,"
                                                                           "\"OpenTime\":\"1970-01-01T00:00:00Z\",\"CloseTime\":\"1970-01-01T00:00:02Z\",\"Count\":4,"
                                                                           "\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/db\"}",
            "double bar wire layout");
    }

    void NativeWireFileJsonMatchesNetByteLayout()
    {
        // "hi" serializes as the byte array [104,105]; Extension/Name precede Value.
        Require(
            std::string(hsm_collector_test_wire_file_json("txt", "n", "hi", "", 1, 1, 0, "p/f")) == "{\"Type\":6,\"Extension\":\"txt\",\"Name\":\"n\",\"Value\":[104,105],\"Comment\":null,"
                                                                                                    "\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/f\"}",
            "file wire layout (List<byte> numeric array)");
    }

#if defined(HSM_COLLECTOR_HTTP)
    void NativeHttpTransportPostsToCaptureServer()
    {
        hsm::test::HttpCaptureServer server(200);
        hsm::http::HttpTransport transport(5000, /*verify_peer=*/false);

        const std::string url = "http://127.0.0.1:" + std::to_string(server.Port()) + "/api/sensors/list";
        const std::string body = "[{\"Type\":1,\"Value\":42,\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/int\"}]";

        const auto response = transport.Post(
            url,
            body,
            { hsm::http::HttpHeader{ "Key", "test-key" },
              hsm::http::HttpHeader{ "ClientName", "test-client" },
              hsm::http::HttpHeader{ "Content-Type", "application/json" } });

        Require(response.transport_ok, ("transport must reach the local capture server: " + response.error).c_str());
        Require(response.IsSuccess(), "expected a 2xx from the capture server");

        for (int i = 0; i < 200 && !server.Request().received.load(std::memory_order_acquire); ++i)
            std::this_thread::sleep_for(std::chrono::milliseconds(5));

        const auto& request = server.Request();
        Require(request.received.load(std::memory_order_acquire), "the capture server must have received the request");
        Require(request.method == "POST", "method must be POST");
        Require(request.path == "/api/sensors/list", "path must be the list endpoint");
        Contains(request.headers, "Key: test-key");
        Contains(request.headers, "ClientName: test-client");
        Require(request.body == body, "captured body must be the exact wire bytes sent");
    }

    // #1097: drive a value through the REAL collector pipeline (add -> queue -> worker -> sender)
    // with the libcurl transport installed, and assert the capture server saw the live POST. This
    // is the end-to-end proof that TestInstallHttpSender wires the transport into the live send
    // path, not just that HttpTransport can POST in isolation (the test above).
    void NativeHttpLiveSendPostsToCaptureServer()
    {
        hsm::test::HttpCaptureServer server(200);

        hsm_collector_options_t options{};
        options.access_key = "live-key";
        // Explicit http:// scheme + AllowPlaintextTransport is what the collector requires for a
        // plaintext target (a bare host defaults to https — MakeEndpoints/Endpoints parity).
        options.server_address = "http://127.0.0.1";
        options.port = server.Port();
        options.client_name = "live-client";
        // module/computer left null so JoinPathParts keeps the wire Path exactly "live/int".
        options.allow_plaintext_transport = true; // the capture server is plaintext HTTP
        options.max_values_in_package = 50;
        options.package_collect_period_ms = 20; // fast dispatch so the worker flushes promptly

        CollectorHandle collector = CreateCollector(options);
        hsm_collector_test_install_http_sender(collector.value);

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");

        SensorHandle sensor = CreateIntSensor(collector.value, "live/int");
        Require(
            hsm_sensor_add_int(sensor.value, 42, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK,
            "add int value failed");

        for (int i = 0; i < 400 && !server.Request().received.load(std::memory_order_acquire); ++i)
            std::this_thread::sleep_for(std::chrono::milliseconds(5));

        const auto& request = server.Request();
        Require(request.received.load(std::memory_order_acquire), "the capture server must have received the live POST");
        Require(request.method == "POST", "live send method must be POST");
        Require(request.path == "/api/sensors/list", "live data batch must route to /list");
        Contains(request.headers, "Key: live-key");
        Contains(request.headers, "ClientName: live-client");
        // The batch is a JSON array of wire values carrying the added int and its path.
        Require(!request.body.empty() && request.body.front() == '[' && request.body.back() == ']', "live body must be a JSON array");
        Contains(request.body, "\"Value\":42");
        Contains(request.body, "\"Path\":\"live/int\"");

        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "collector stop failed");
    }
#endif

    void NativeWireTimeSpanAndVersionMatchNet()
    {
        // .NET TimeSpan "c": "1.02:03:04.0050000" (ticks for TimeSpan(1,2,3,4,5) = 937840050000);
        // day omitted when zero; seven-digit fraction (not trimmed); negative sign.
        Require(std::string(hsm_collector_test_timespan_c_format(937840050000LL)) == "1.02:03:04.0050000", "timespan c-format");
        Require(std::string(hsm_collector_test_timespan_c_format(40000000LL)) == "00:00:04", "timespan whole seconds drops the fraction and day");
        Require(std::string(hsm_collector_test_timespan_c_format(-600000000LL)) == "-00:01:00", "timespan negative");
        // TimeSpan.MinValue.Ticks == Int64.MinValue is a legal value; negating it as int64 is UB.
        // Unsigned-magnitude formatting must yield the .NET "c" string exactly.
        Require(std::string(hsm_collector_test_timespan_c_format(INT64_MIN)) == "-10675199.02:48:05.4775808", "timespan Int64.MinValue (no UB)");

        // TimeSpan(7) / Version(8) value DTOs serialize the formatted text as a quoted string.
        Require(
            std::string(hsm_collector_test_wire_value_json(7, "\"1.02:03:04.0050000\"", "", 1, 1, 0, "p/ts")) == "{\"Type\":7,\"Value\":\"1.02:03:04.0050000\",\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/ts\"}",
            "timespan wire value");
        Require(
            std::string(hsm_collector_test_wire_value_json(8, "\"1.2.3.4\"", "", 1, 1, 0, "p/v")) == "{\"Type\":8,\"Value\":\"1.2.3.4\",\"Comment\":null,\"Time\":\"1970-01-01T00:00:00Z\",\"Status\":1,\"Key\":null,\"Path\":\"p/v\"}",
            "version wire value");
    }

    void NativeWireRegistrationJsonMatchesNetByteLayout()
    {
        // SensorType IntSensor(1); ttl 60000ms -> 600000000 ticks; OriginalUnit MB(3);
        // EnumOption wire order Key,Value,Description,Color (Color ARGB int).
        Require(
            std::string(hsm_collector_test_wire_registration_json(
                1, 60000, 3, 1, "d", 1, 1, "v", "ed", -16711936, "p/int")) == "{\"Type\":0,\"Alerts\":null,\"TtlAlerts\":null,\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\","
                                                                              "\"DefaultChats\":null,\"KeepHistory\":null,\"SelfDestroy\":null,\"TTLs\":[600000000],\"TTL\":null,"
                                                                              "\"Statistics\":0,\"IsSingletonSensor\":null,\"AggregateData\":null,\"EnableGrafana\":null,"
                                                                              "\"OriginalUnit\":3,\"DisplayUnit\":0,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,"
                                                                              "\"EnumOptions\":[{\"Key\":1,\"Value\":\"v\",\"Description\":\"ed\",\"Color\":-16711936}],"
                                                                              "\"Key\":null,\"Path\":\"p/int\"}",
            "AddOrUpdateSensorRequest wire layout");
    }

    // Build the canonical alert scenario through the C ABI and assert the sensor's wire
    // registration is byte-identical to the .NET golden (WireFormatGoldenLockTests
    // .Registration_with_alerts_matches_the_native_golden_bytes). Locks AlertUpdateRequest field
    // order, numeric enums, the AlertIcon->emoji map escaped to ⚠, ConfirmationPeriod ticks,
    // the LastValue target serialized null, and the TTL-alert -> TTLs coupling.
    void NativeWireRegistrationWithAlertsMatchesNetByteLayout()
    {
        hsm_collector_options_t options{};
        options.access_key = "test-key";
        options.server_address = "https://localhost";
        options.port = 443;
        // Empty module/computer_name so the registered path is exactly "p/alert".
        auto collector = CreateCollector(options);

        SensorHandle sensor;
        // Description "d", OriginalUnit MB(3), no plain TTL (the TTL alert drives TTLs).
        Require(
            hsm_collector_create_int_sensor_with_options(collector.value, "p/alert", 0, 3, "d", &sensor.value) == HSM_RESULT_OK,
            "alert sensor create");

        hsm_alert_t* data_alert = nullptr;
        Require(hsm_collector_create_alert(collector.value, HSM_ALERT_KIND_INSTANT, &data_alert) == HSM_RESULT_OK, "data alert create");
        hsm_alert_add_condition(data_alert, HSM_ALERT_COMBINATION_AND, HSM_ALERT_PROP_VALUE, HSM_ALERT_OP_GREATER_THAN, HSM_ALERT_TARGET_CONST, "42");
        hsm_alert_add_condition(data_alert, HSM_ALERT_COMBINATION_OR, HSM_ALERT_PROP_STATUS, HSM_ALERT_OP_IS_OK, HSM_ALERT_TARGET_LAST_VALUE, nullptr);
        hsm_alert_set_sensor_error(data_alert);
        hsm_alert_set_notification(data_alert, "spike", HSM_ALERT_DESTINATION_ALL_CHATS);
        hsm_alert_set_icon(data_alert, HSM_ALERT_ICON_WARNING);
        hsm_alert_set_confirmation_period(data_alert, 300000); // 300000 ms -> 3000000000 ticks
        Require(hsm_sensor_attach_alert(sensor.value, data_alert) == HSM_RESULT_OK, "attach data alert");

        hsm_alert_t* ttl_alert = nullptr;
        Require(hsm_collector_create_alert(collector.value, HSM_ALERT_KIND_TTL, &ttl_alert) == HSM_RESULT_OK, "ttl alert create");
        hsm_alert_set_inactivity_period(ttl_alert, 60000); // 60000 ms -> TTLs [600000000]
        hsm_alert_set_notification(ttl_alert, "inactive", HSM_ALERT_DESTINATION_FROM_PARENT);
        Require(hsm_sensor_attach_alert(sensor.value, ttl_alert) == HSM_RESULT_OK, "attach ttl alert");

        Require(
            std::string(hsm_sensor_test_wire_registration_json(sensor.value)) ==
                "{\"Type\":0,\"Alerts\":[{\"Conditions\":[{\"Combination\":0,\"Operation\":2,\"Property\":20,\"Target\":{\"Type\":0,\"Value\":\"42\"}},"
                "{\"Combination\":1,\"Operation\":22,\"Property\":0,\"Target\":{\"Type\":1,\"Value\":null}}],"
                "\"Status\":3,\"DestinationMode\":200,\"Template\":\"spike\",\"Icon\":\"\\u26A0\",\"IsDisabled\":false,"
                "\"ConfirmationPeriod\":3000000000,\"ScheduledNotificationTime\":null,\"ScheduledRepeatMode\":null,\"ScheduledInstantSend\":null}],"
                "\"TtlAlerts\":[{\"Conditions\":[],\"Status\":1,\"DestinationMode\":3,\"Template\":\"inactive\",\"Icon\":null,\"IsDisabled\":false,"
                "\"ConfirmationPeriod\":null,\"ScheduledNotificationTime\":null,\"ScheduledRepeatMode\":null,\"ScheduledInstantSend\":null}],"
                "\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\",\"DefaultChats\":null,\"KeepHistory\":null,\"SelfDestroy\":null,"
                "\"TTLs\":[600000000],\"TTL\":null,\"Statistics\":0,\"IsSingletonSensor\":null,\"AggregateData\":null,\"EnableGrafana\":null,"
                "\"OriginalUnit\":3,\"DisplayUnit\":0,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,\"EnumOptions\":null,"
                "\"Key\":null,\"Path\":\"p/alert\"}",
            "AddOrUpdateSensorRequest wire layout with alerts");
    }

    // Full SensorOptions surface (#1098 §6) through hsm_collector_create_sensor_with_options; the
    // sensor's wire registration must be byte-identical to the .NET golden
    // (WireFormatGoldenLockTests.Registration_full_options_match_the_native_golden_bytes).
    void NativeWireRegistrationFullOptionsMatchesNetByteLayout()
    {
        hsm_collector_options_t collector_options{};
        collector_options.access_key = "test-key";
        collector_options.server_address = "https://localhost";
        collector_options.port = 443;
        // Empty module/computer so the path stays exactly "comp/mod/full/opts" (the bare user path).
        auto collector = CreateCollector(collector_options);

        hsm_sensor_options_t options{};
        options.ttl_ms = 60000; // -> TTLs [600000000]
        options.unit = 3;       // MB
        options.description = "d";
        options.keep_history_ms = 600000;  // -> KeepHistory 6000000000
        options.self_destroy_ms = 1200000; // -> SelfDestroy 12000000000
        options.display_unit = 3;
        options.statistics = 1; // EMA
        options.is_singleton = 1;
        options.aggregate_data = 1;
        options.enable_grafana = 1;
        options.is_computer_sensor = false;
        options.sensor_location = 0; // Module

        SensorHandle sensor;
        Require(
            hsm_collector_create_sensor_with_options(collector.value, "comp/mod/full/opts", HSM_SENSOR_TYPE_INT, &options, &sensor.value) == HSM_RESULT_OK,
            "full options sensor create");

        Require(
            std::string(hsm_sensor_test_wire_registration_json(sensor.value)) ==
                "{\"Type\":0,\"Alerts\":null,\"TtlAlerts\":null,\"TtlAlert\":null,\"SensorType\":1,\"Description\":\"d\","
                "\"DefaultChats\":null,\"KeepHistory\":6000000000,\"SelfDestroy\":12000000000,\"TTLs\":[600000000],\"TTL\":null,"
                "\"Statistics\":1,\"IsSingletonSensor\":true,\"AggregateData\":true,\"EnableGrafana\":true,"
                "\"OriginalUnit\":3,\"DisplayUnit\":3,\"DefaultAlertsOptions\":0,\"IsForceUpdate\":false,\"EnumOptions\":null,"
                "\"Key\":null,\"Path\":\"comp/mod/full/opts\"}",
            "AddOrUpdateSensorRequest wire layout with full options");
    }

    // Scheduled-notification alert (ThenSendScheduledNotification) — ScheduledNotificationTime is
    // ISO-8601-Z, ScheduledRepeatMode/InstantSend are emitted. Pinned against the .NET golden
    // (WireFormatGoldenLockTests.Scheduled_notification_alert_matches_the_native_golden_bytes).
    // NOTE: native always renders Z (UTC); the managed DateTime's Kind drives its suffix, so a
    // non-UTC ScheduledNotificationTime would differ — the collector schedule API uses UTC.
    void NativeAlertScheduledNotificationMatchesNet()
    {
        auto collector = CreateCollector();
        hsm_alert_t* alert = nullptr;
        Require(hsm_collector_create_alert(collector.value, HSM_ALERT_KIND_INSTANT, &alert) == HSM_RESULT_OK, "create alert");
        Require(
            hsm_alert_set_scheduled_notification(alert, "sched", 1500, HSM_ALERT_REPEAT_HOURLY, true, HSM_ALERT_DESTINATION_FROM_PARENT) == HSM_RESULT_OK,
            "set scheduled notification");
        Require(
            std::string(hsm_alert_test_wire_json(alert)) ==
                "{\"Conditions\":[],\"Status\":1,\"DestinationMode\":3,\"Template\":\"sched\",\"Icon\":null,\"IsDisabled\":false,"
                "\"ConfirmationPeriod\":null,\"ScheduledNotificationTime\":\"1970-01-01T00:00:01.5Z\",\"ScheduledRepeatMode\":20,\"ScheduledInstantSend\":true}",
            "scheduled notification alert wire layout");
    }

    // Version.ToString() rules: trailing absent (-1) components are dropped, major.minor is the floor.
    void NativeVersionStringMatchesNet()
    {
        auto collector = CreateCollector();
        SensorHandle sensor;
        Require(hsm_collector_create_version_sensor(collector.value, "native/version/fmt", &sensor.value) == HSM_RESULT_OK, "version sensor create");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        Require(hsm_sensor_add_version(sensor.value, 1, 2, 3, 4, HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "add 1.2.3.4");
        Require(hsm_sensor_add_version(sensor.value, 1, 2, -1, -1, HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "add 1.2");
        Require(hsm_sensor_add_version(sensor.value, 2, 0, 5, -1, HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "add 2.0.5");
        Require(WaitForSentCountEquals(collector.value, 3), "version values dispatched");

        Require(SentJson(collector.value, 0).find("\"Value\":\"1.2.3.4\"") != std::string::npos, "1.2.3.4");
        Require(SentJson(collector.value, 1).find("\"Value\":\"1.2\"") != std::string::npos, "1.2");
        Require(SentJson(collector.value, 2).find("\"Value\":\"2.0.5\"") != std::string::npos, "2.0.5 (revision dropped)");
    }

    // Prototype merge (DefaultPrototype.Merge): identity (is_computer) is PINNED by the prototype;
    // metadata (TTL, Description) takes the custom value when set, else the prototype default.
    void NativePrototypeMergePinsIdentityOverridesMetadata()
    {
        // Custom overrides TTL (120000 ms -> 1200000000 ticks) and Description; prototype's
        // is_computer (=> IsSingletonSensor:true) is pinned and cannot be overridden.
        const std::string overridden =
            hsm_collector_test_merge_registration_json(1, 60000, "proto", 120000, "custom", 1, "merge/test");
        Require(overridden.find("\"TTLTicks\":[1200000000]") != std::string::npos, "custom TTL overrides prototype");
        Require(overridden.find("\"Description\":\"custom\"") != std::string::npos, "custom description overrides prototype");
        Require(overridden.find("\"IsSingletonSensor\":true") != std::string::npos, "prototype is_computer pinned");

        // Custom leaves TTL (0) and Description unset -> both fall back to the prototype defaults.
        const std::string fallback =
            hsm_collector_test_merge_registration_json(1, 60000, "proto", 0, nullptr, 0, "merge/test");
        Require(fallback.find("\"TTLTicks\":[600000000]") != std::string::npos, "prototype TTL retained when custom unset");
        Require(fallback.find("\"Description\":\"proto\"") != std::string::npos, "prototype description retained when custom unset");
        Require(fallback.find("\"IsSingletonSensor\":true") != std::string::npos, "prototype is_computer still pinned");
    }

    void NativeHttpEndpointRoutingMatchesNet()
    {
        // Scheme defaulting mirrors Endpoints(CollectorOptions): bare host -> https; explicit
        // http stays http ONLY with AllowPlaintextTransport, otherwise upgraded to https; the
        // /api/sensors base path and explicit Port are always applied.
        Require(hsm::http::MakeEndpoints("localhost", 44330, false).connection_address ==
                    "https://localhost:44330/api/sensors",
                "bare host defaults to https");
        Require(hsm::http::MakeEndpoints("https://example.org", 443, false).connection_address ==
                    "https://example.org:443/api/sensors",
                "explicit https preserved");
        Require(hsm::http::MakeEndpoints("http://example.org", 80, false).connection_address ==
                    "https://example.org:80/api/sensors",
                "explicit http upgraded to https without plaintext opt-in");
        Require(hsm::http::MakeEndpoints("http://example.org", 8080, true).connection_address ==
                    "http://example.org:8080/api/sensors",
                "explicit http kept only with plaintext opt-in");
        // An embedded :port/path on the URL is dropped — the explicit Port wins (UriBuilder parity).
        Require(hsm::http::MakeEndpoints("https://host:1234/ignored", 44330, false).connection_address ==
                    "https://host:44330/api/sensors",
                "embedded port/path dropped, explicit port applied");

        const auto ep = hsm::http::MakeEndpoints("localhost", 44330, false);

        // Single-value routes (DataHandlers.GetUri), one per kind.
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_BOOLEAN, false) == ep.connection_address + "/bool", "bool route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_INT, false) == ep.connection_address + "/int", "int route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_DOUBLE, false) == ep.connection_address + "/double", "double route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_STRING, false) == ep.connection_address + "/string", "string route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_INT_BAR, false) == ep.connection_address + "/intBar", "intBar route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_DOUBLE_BAR, false) == ep.connection_address + "/doubleBar", "doubleBar route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_FILE, false) == ep.connection_address + "/file", "file route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_RATE, false) == ep.connection_address + "/rate", "rate route");
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_ENUM, false) == ep.connection_address + "/enum", "enum route");

        // A batch of values goes to /list regardless of kind.
        Require(hsm::http::RouteForSensorValue(ep, HSM_SENSOR_TYPE_BOOLEAN, true) == ep.connection_address + "/list", "value batch -> /list");

        // Commands: batch -> /commands, single AddOrUpdate -> /addOrUpdate.
        Require(hsm::http::RouteForCommand(ep, true) == ep.connection_address + "/commands", "command batch -> /commands");
        Require(hsm::http::RouteForCommand(ep, false) == ep.connection_address + "/addOrUpdate", "single command -> /addOrUpdate");

        Require(ep.TestConnection() == ep.connection_address + "/testConnection", "testConnection route");
    }

    void NativeHttpRetryPolicyMatchesNet()
    {
        const auto data = hsm::http::RetryPolicy::Data();         // bounded, exponential, 10 retries
        const auto commands = hsm::http::RetryPolicy::Commands(); // unbounded, linear

        // ShouldRetry parity with BaseHandlers.ShouldRetry (#1096 fix):
        // transport failure (exception-equivalent) is always retried on both pipelines.
        Require(data.ShouldRetry(/*transport_ok=*/false, 0), "data retries transport failure");
        Require(commands.ShouldRetry(/*transport_ok=*/false, 0), "commands retry transport failure");

        // 5xx is retried only on the bounded pipeline; the unbounded command pipeline must not
        // spin forever on a persistent server error.
        Require(data.ShouldRetry(true, 503), "data retries 5xx");
        Require(!commands.ShouldRetry(true, 503), "commands do NOT retry 5xx (would hang unbounded)");

        // 4xx is permanent on both; 2xx is success on both.
        Require(!data.ShouldRetry(true, 400), "data does not retry 4xx");
        Require(!data.ShouldRetry(true, 404), "data does not retry 4xx");
        Require(!data.ShouldRetry(true, 200), "2xx is success");
        Require(!commands.ShouldRetry(true, 400), "commands do not retry 4xx");

        // Attempt budget: bounded stops after 10 retries; unbounded never exhausts.
        Require(data.HasAttemptsLeft(10), "10th retry allowed");
        Require(!data.HasAttemptsLeft(11), "11th retry rejected");
        Require(commands.HasAttemptsLeft(1000000), "command retries never exhaust");

        // Exponential backoff: 1s, 2s, 4s, 8s ... clamped at 2min (Polly base*2^n, MaxDelay=120s).
        Require(data.DelayMs(0) == 1000, "exp retry#1 = 1s");
        Require(data.DelayMs(1) == 2000, "exp retry#2 = 2s");
        Require(data.DelayMs(2) == 4000, "exp retry#3 = 4s");
        Require(data.DelayMs(3) == 8000, "exp retry#4 = 8s");
        Require(data.DelayMs(7) == 120000, "exp clamps to 2min (128s -> 120s)");
        Require(data.DelayMs(20) == 120000, "exp stays clamped at 2min");

        // Linear backoff: 1s, 2s, 3s ... clamped at 2min (Polly base*(n+1)).
        Require(commands.DelayMs(0) == 1000, "lin retry#1 = 1s");
        Require(commands.DelayMs(1) == 2000, "lin retry#2 = 2s");
        Require(commands.DelayMs(2) == 3000, "lin retry#3 = 3s");
        Require(commands.DelayMs(119) == 120000, "lin retry#120 = 2min");
        Require(commands.DelayMs(200) == 120000, "lin clamps to 2min");
    }

    // Concatenate every recorded payload so a test can assert which values survived the queue.
    std::string AllSentPayloads(hsm_collector_t* collector)
    {
        std::string all;
        const auto count = hsm_collector_sent_count(collector);
        for (size_t index = 0; index < count; ++index)
        {
            all += SentJson(collector, index);
            all += '\n';
        }

        return all;
    }

    // Build options with explicit pipeline limits (mirrors create_collector_with_limits): a
    // recording sender, a small package size, and a short collect period for prompt dispatch.
    hsm_collector_options_t LimitsOptions(int32_t max_queue_size, int32_t max_values_in_package, int32_t period_ms)
    {
        auto options = TestOptions();
        options.max_queue_size = max_queue_size;
        options.max_values_in_package = max_values_in_package;
        options.package_collect_period_ms = period_ms;
        return options;
    }

    // #1088 (retry meets a FULL buffer): a failed batch that comes back to an already-full queue is
    // dropped — the retry is the oldest data, so it is dropped rather than evicting a queued value.
    // This is the ONLY case a retry is ever dropped; below capacity the retry is always kept (see
    // NativeRetryBelowCapacityIsAlwaysRedelivered).
    //
    // Deterministic staging via the hang seam: the send of the first batch hangs in flight, holding
    // it "out" of the queue while the queue is refilled to capacity, then the hang is released as a
    // failure so the batch re-enqueues into a full queue.
    void NativeRetryMeetingFullQueueIsDroppedNotEvictingQueuedValues()
    {
        // queue cap 4, batch 2, short period. Values: 1,2 (first batch, hangs in flight) | 3,4 (left
        // queued) | 5,6 (added while [1,2] hangs) -> buffer full at 4 when [1,2] comes back.
        const auto options = LimitsOptions(/*max_queue_size=*/4, /*max_values_in_package=*/2, /*period_ms=*/20);
        CollectorHandle collector = CreateCollector(options);
        SensorHandle sensor = CreateIntSensor(collector.value, "pipeline/1088");

        hsm_collector_set_send_hang(collector.value, true);
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");

        for (int value : { 1, 2, 3, 4 })
            Require(hsm_sensor_add_int(sensor.value, value, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add failed");

        // Generous barrier over the 20ms period: the worker has woken, popped [1,2], and is now
        // hanging in flight. 3,4 remain queued.
        std::this_thread::sleep_for(std::chrono::milliseconds(300));

        // Added while the first batch hangs -> these fill the queue back to capacity (4).
        for (int value : { 5, 6 })
            Require(hsm_sensor_add_int(sensor.value, value, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add failed");

        // Release the hang AS A FAILURE so [1,2] re-enqueues into a FULL queue -> #1088 backstop
        // drops both (they are the oldest data). Then 3,4,5,6 send cleanly.
        hsm_collector_set_send_fail_next(collector.value, 1);
        hsm_collector_set_send_hang(collector.value, false);

        Require(WaitForSentCountEquals(collector.value, 4), "exactly the four survivors should be delivered");

        const auto sent = AllSentPayloads(collector.value);
        NotContains(sent, "\"Value\":1,");
        NotContains(sent, "\"Value\":2,");
        Contains(sent, "\"Value\":3,");
        Contains(sent, "\"Value\":4,");
        Contains(sent, "\"Value\":5,");
        Contains(sent, "\"Value\":6,");

        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
    }

    // Monitoring-history contract: BELOW capacity a failed retry is NEVER dropped, even when fresher
    // values arrived while its send was in flight. Every value is delivered (at-least-once); the only
    // drop path is a full buffer (NativeRetryMeetingFullQueueIsDroppedNotEvictingQueuedValues).
    void NativeRetryBelowCapacityIsAlwaysRedelivered()
    {
        // Huge cap (capacity never bites), batch 1 so value 1 is the lone in-flight retry. 2,3 arrive
        // while 1's send hangs — under the old #1090 filter 1 would have been dropped; now it is kept.
        const auto options = LimitsOptions(/*max_queue_size=*/20000, /*max_values_in_package=*/1, /*period_ms=*/20);
        CollectorHandle collector = CreateCollector(options);
        SensorHandle sensor = CreateIntSensor(collector.value, "pipeline/at-least-once");

        hsm_collector_set_send_hang(collector.value, true);
        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start failed");

        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add failed");

        // Barrier: the worker has popped [1] and is hanging in flight.
        std::this_thread::sleep_for(std::chrono::milliseconds(300));

        // Fresher values arrive while 1 is in flight (and become the queue head).
        for (int value : { 2, 3 })
            Require(hsm_sensor_add_int(sensor.value, value, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_OK, "add failed");

        // Release as a failure: [1] re-enqueues. The buffer is far below capacity, so 1 is KEPT and
        // retried — all three values are delivered, none dropped.
        hsm_collector_set_send_fail_next(collector.value, 1);
        hsm_collector_set_send_hang(collector.value, false);

        Require(WaitForSentCountEquals(collector.value, 3), "all three values must be delivered (at-least-once)");

        const auto sent = AllSentPayloads(collector.value);
        Contains(sent, "\"Value\":1,");
        Contains(sent, "\"Value\":2,");
        Contains(sent, "\"Value\":3,");

        Require(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop failed");
    }

    const std::map<std::string, std::function<void(const std::string&)>>& Tests()
    {
        static const std::map<std::string, std::function<void(const std::string&)>> tests = {
#if defined(HSM_COLLECTOR_HTTP)
            { "native_http_transport_posts_to_capture_server", [](const std::string&) { NativeHttpTransportPostsToCaptureServer(); } },
            { "native_http_live_send_posts_to_capture_server", [](const std::string&) { NativeHttpLiveSendPostsToCaptureServer(); } },
#endif
            { "native_http_endpoint_routing_matches_net", [](const std::string&) { NativeHttpEndpointRoutingMatchesNet(); } },
            { "native_http_retry_policy_matches_net", [](const std::string&) { NativeHttpRetryPolicyMatchesNet(); } },
            { "native_wire_timespan_and_version_match_net", [](const std::string&) { NativeWireTimeSpanAndVersionMatchNet(); } },
            { "native_wire_registration_json_matches_net_byte_layout", [](const std::string&) { NativeWireRegistrationJsonMatchesNetByteLayout(); } },
            { "native_wire_registration_with_alerts_matches_net_byte_layout", [](const std::string&) { NativeWireRegistrationWithAlertsMatchesNetByteLayout(); } },
            { "native_wire_registration_full_options_matches_net_byte_layout", [](const std::string&) { NativeWireRegistrationFullOptionsMatchesNetByteLayout(); } },
            { "native_version_string_matches_net", [](const std::string&) { NativeVersionStringMatchesNet(); } },
            { "native_alert_scheduled_notification_matches_net", [](const std::string&) { NativeAlertScheduledNotificationMatchesNet(); } },
            { "native_prototype_merge_pins_identity_overrides_metadata", [](const std::string&) { NativePrototypeMergePinsIdentityOverridesMetadata(); } },
            { "native_wire_iso_from_unix_ms_matches_net", [](const std::string&) { NativeWireIsoFromUnixMsMatchesNet(); } },
            { "native_wire_value_json_matches_net_byte_layout", [](const std::string&) { NativeWireValueJsonMatchesNetByteLayout(); } },
            { "native_wire_bar_json_matches_net_byte_layout", [](const std::string&) { NativeWireBarJsonMatchesNetByteLayout(); } },
            { "native_wire_file_json_matches_net_byte_layout", [](const std::string&) { NativeWireFileJsonMatchesNetByteLayout(); } },
            { "native_lifecycle_listener_can_register_another_listener", [](const std::string&) { NativeLifecycleListenerCanRegisterAnotherListener(); } },
            { "native_logger_deduplicates_repeated_errors_within_window", [](const std::string&) { NativeLoggerDeduplicatesRepeatedErrorsWithinWindow(); } },
            { "native_logger_zero_window_logs_every_error", [](const std::string&) { NativeLoggerZeroWindowLogsEveryError(); } },
            { "native_scheduler_clock_seam_drives_periodic_posts", [](const std::string&) { NativeSchedulerClockSeamDrivesPeriodicPosts(); } },
            { "native_scheduler_on_error_isolates_throwing_callback", [](const std::string&) { NativeSchedulerOnErrorIsolatesThrowingCallback(); } },
            { "native_version_matches_macro", [](const std::string&) { NativeVersionMatchesMacro(); } },
            { "native_status_tracks_lifecycle", [](const std::string&) { NativeStatusTracksLifecycle(); } },
            { "native_dispose_is_terminal_and_idempotent", [](const std::string&) { NativeDisposeIsTerminalAndIdempotent(); } },
            { "native_dispose_from_running_stops", [](const std::string&) { NativeDisposeFromRunningStops(); } },
            { "native_lifecycle_listener_receives_transitions", [](const std::string&) { NativeLifecycleListenerReceivesTransitions(); } },
            { "native_lifecycle_listener_exception_is_isolated", [](const std::string&) { NativeLifecycleListenerExceptionIsIsolated(); } },
            { "native_test_connection_reports_reachable", [](const std::string&) { NativeTestConnectionReportsReachable(); } },
            { "native_max_sensors_cap_rejects_beyond_limit", [](const std::string&) { NativeMaxSensorsCapRejectsBeyondLimit(); } },
            { "native_create_rejects_negative_option_fields", [](const std::string&) { NativeCreateRejectsNegativeOptionFields(); } },
            { "native_logger_sink_can_be_set_and_cleared", [](const std::string&) { NativeLoggerSinkCanBeSetAndCleared(); } },
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
            { "native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values", [](const std::string&) { NativeRetryMeetingFullQueueIsDroppedNotEvictingQueuedValues(); } },
            { "native_retry_below_capacity_is_always_redelivered", [](const std::string&) { NativeRetryBelowCapacityIsAlwaysRedelivered(); } },
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
            { "conformance_registration_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_timespan_version_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_alert_registration_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_options_surface_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_service_commands_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "meta_must_fail", [](const std::string& path) { RunConformanceContractExpectFailure(path); } },
            { "conformance_fuzz", [](const std::string& path) { RunConformanceContract(path); } },
        };

        return tests;
    }
} // namespace

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
}
