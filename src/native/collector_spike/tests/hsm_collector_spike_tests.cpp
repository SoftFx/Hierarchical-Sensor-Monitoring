#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"

#include <iostream>
#include <fstream>
#include <functional>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
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

    SensorHandle CreateIntSensor(hsm_collector_t* collector, const char* path)
    {
        SensorHandle sensor;

        Require(hsm_collector_create_int_sensor(collector, path, &sensor.value) == HSM_RESULT_OK, "sensor create failed");

        return sensor;
    }

    std::string SentJson(hsm_collector_t* collector, size_t index)
    {
        const char* json = nullptr;

        Require(hsm_collector_get_sent_json(collector, index, &json) == HSM_RESULT_OK, "payload lookup failed");

        return std::string{ json };
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

    std::string ExpandTextToken(const std::string& value)
    {
        const std::string repeat_prefix = "repeat:";
        if (value.rfind(repeat_prefix, 0) != 0)
            return value;

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
    };

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
            state.sensors.push_back(CreateIntSensor(state.collector.value, step[1].c_str()));
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

        if (action == "expect_sent_count")
        {
            Require(step.size() >= 2, "expect_sent_count requires count");
            const auto expected = static_cast<size_t>(ToInt(step[1]));
            Require(hsm_collector_sent_count(state.collector.value) == expected, "sent count did not match");
            return;
        }

        if (action == "expect_payload_contains")
        {
            Require(step.size() >= 3, "expect_payload_contains requires index and substring");
            const auto payload = SentJson(state.collector.value, static_cast<size_t>(ToInt(step[1])));
            Contains(payload, step[2]);
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

    void CAbi_BeforeStartDropsValue()
    {
        auto collector = CreateCollector();
        auto sensor = CreateIntSensor(collector.value, "spike/int");

        Require(hsm_sensor_add_int(sensor.value, 7, HSM_SENSOR_STATUS_OK, "before start") == HSM_RESULT_OK, "add before start failed");
        Require(hsm_collector_sent_count(collector.value) == 0, "stopped collector should drop values");
    }

    void CAbi_RunningCollectorStoresIntPayload()
    {
        auto collector = CreateCollector();
        auto sensor = CreateIntSensor(collector.value, "spike/int");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 42, HSM_SENSOR_STATUS_WARNING, "watch") == HSM_RESULT_OK, "add value failed");
        Require(hsm_collector_sent_count(collector.value) == 1, "running collector should store one payload");

        const auto payload = SentJson(collector.value, 0);
        Contains(payload, "\"Type\":1");
        Contains(payload, "\"Path\":\"conformance-host/conformance-module/spike/int\"");
        Contains(payload, "\"Value\":42");
        Contains(payload, "\"Status\":2");
        Contains(payload, "\"Comment\":\"watch\"");
        Contains(payload, "\"UnixTimeMs\":");
    }

    void CAbi_DuplicateSensorPathIsIdempotent()
    {
        auto collector = CreateCollector();
        auto first_sensor = CreateIntSensor(collector.value, "spike/duplicate");
        auto second_sensor = CreateIntSensor(collector.value, "spike/duplicate");

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(first_sensor.value, 1, HSM_SENSOR_STATUS_OK, "first") == HSM_RESULT_OK, "first add failed");
        Require(hsm_sensor_add_int(second_sensor.value, 2, HSM_SENSOR_STATUS_OK, "second") == HSM_RESULT_OK, "second add failed");
        Require(hsm_collector_sent_count(collector.value) == 2, "duplicate path handles should send through one registered sensor");

        Contains(SentJson(collector.value, 0), "\"Path\":\"conformance-host/conformance-module/spike/duplicate\"");
        Contains(SentJson(collector.value, 1), "\"Path\":\"conformance-host/conformance-module/spike/duplicate\"");
    }

    void CAbi_LongCommentIsTrimmed()
    {
        auto collector = CreateCollector();
        auto sensor = CreateIntSensor(collector.value, "spike/comment");
        const std::string long_comment(1100, 'a');

        Require(hsm_collector_start(collector.value) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor.value, 1, HSM_SENSOR_STATUS_OK, long_comment.c_str()) == HSM_RESULT_OK, "add failed");

        const auto payload = SentJson(collector.value, 0);
        const auto comment = CommentFromPayload(payload);

        Require(comment.size() == 1024, "comment should be trimmed to the managed collector limit");
    }

    void CAbi_InvalidArgumentsReturnErrors()
    {
        auto options = TestOptions();
        hsm_collector_t* collector = nullptr;
        hsm_sensor_t* sensor = nullptr;

        Require(hsm_collector_create(nullptr, &collector) == HSM_RESULT_INVALID_ARGUMENT, "null options should fail");
        Require(hsm_collector_create(&options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "null collector out should fail");
        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create failed");
        CollectorHandle collector_guard{ collector };

        Require(hsm_collector_create_int_sensor(nullptr, "spike/int", &sensor) == HSM_RESULT_INVALID_ARGUMENT, "null collector should fail");
        Require(hsm_collector_create_int_sensor(collector, "spike/int", nullptr) == HSM_RESULT_INVALID_ARGUMENT, "null sensor out should fail");
        Require(hsm_sensor_add_int(nullptr, 1, HSM_SENSOR_STATUS_OK, "") == HSM_RESULT_INVALID_ARGUMENT, "null sensor should fail");
    }

    void CAbi_MissingPayloadReturnsNotFound()
    {
        auto collector = CreateCollector();
        const char* json = nullptr;

        Require(hsm_collector_get_sent_json(collector.value, 0, &json) == HSM_RESULT_NOT_FOUND, "missing payload should return not found");
        Require(json == nullptr, "missing payload should clear output pointer");
    }

    void CppWrapper_CreatesSensorAndReadsSentPayload()
    {
        hsm::collector_spike::Collector collector({
            "test-key",
            "https://localhost",
            443,
            "native-spike",
            "native-host",
        });

        auto sensor = collector.CreateIntSensor("wrapper/int");

        collector.Start();
        sensor.AddValue(100, hsm::collector_spike::SensorStatus::Ok, "from wrapper");
        collector.Stop();
        sensor.AddValue(200);

        Require(collector.SentCount() == 1, "stopped collector should drop values after Stop");

        const auto payload = collector.SentJson(0);
        Contains(payload, "\"Path\":\"native-host/native-spike/wrapper/int\"");
        Contains(payload, "\"Value\":100");
        Contains(payload, "\"Status\":1");
    }

    void CppWrapper_ReportsInvalidPath()
    {
        hsm::collector_spike::Collector collector({
            "test-key",
            "https://localhost",
            443,
            "native-spike",
            "native-host",
        });

        try
        {
            (void)collector.CreateIntSensor("");
        }
        catch (const hsm::collector_spike::Error& ex)
        {
            Contains(ex.what(), "Sensor path must not be empty.");
            return;
        }

        throw std::runtime_error("Expected invalid path error");
    }

    void CppWrapper_StartTwiceReportsInvalidState()
    {
        hsm::collector_spike::Collector collector({
            "test-key",
            "https://localhost",
            443,
            "native-spike",
            "native-host",
        });

        collector.Start();

        try
        {
            collector.Start();
        }
        catch (const hsm::collector_spike::Error& ex)
        {
            Contains(ex.what(), "Collector is already running.");
            return;
        }

        throw std::runtime_error("Expected duplicate Start error");
    }

    const std::map<std::string, std::function<void(const std::string&)>>& Tests()
    {
        static const std::map<std::string, std::function<void(const std::string&)>> tests = {
            { "c_abi_before_start_drops_value", [](const std::string&) { CAbi_BeforeStartDropsValue(); } },
            { "c_abi_running_collector_stores_int_payload", [](const std::string&) { CAbi_RunningCollectorStoresIntPayload(); } },
            { "c_abi_duplicate_sensor_path_is_idempotent", [](const std::string&) { CAbi_DuplicateSensorPathIsIdempotent(); } },
            { "c_abi_long_comment_is_trimmed", [](const std::string&) { CAbi_LongCommentIsTrimmed(); } },
            { "c_abi_invalid_arguments_return_errors", [](const std::string&) { CAbi_InvalidArgumentsReturnErrors(); } },
            { "c_abi_missing_payload_returns_not_found", [](const std::string&) { CAbi_MissingPayloadReturnsNotFound(); } },
            { "cpp_wrapper_creates_sensor_and_reads_payload", [](const std::string&) { CppWrapper_CreatesSensorAndReadsSentPayload(); } },
            { "cpp_wrapper_reports_invalid_path", [](const std::string&) { CppWrapper_ReportsInvalidPath(); } },
            { "cpp_wrapper_start_twice_reports_invalid_state", [](const std::string&) { CppWrapper_StartTwiceReportsInvalidState(); } },
            { "conformance_instant_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
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

        for (const auto& test : tests)
        {
            if (test.first == "conformance_instant_int_contract")
                continue;

            test.second(std::string{});
        }
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }

    return 0;
}
