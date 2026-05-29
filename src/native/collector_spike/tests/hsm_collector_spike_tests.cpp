#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"

#include <iostream>
#include <functional>
#include <map>
#include <stdexcept>
#include <string>
#include <utility>

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
        options.module = "native-spike";
        options.computer_name = "native-host";
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
        Contains(payload, "\"Path\":\"spike/int\"");
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

        Contains(SentJson(collector.value, 0), "\"Path\":\"spike/duplicate\"");
        Contains(SentJson(collector.value, 1), "\"Path\":\"spike/duplicate\"");
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
        Contains(payload, "\"Path\":\"wrapper/int\"");
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

    const std::map<std::string, std::function<void()>>& Tests()
    {
        static const std::map<std::string, std::function<void()>> tests = {
            { "c_abi_before_start_drops_value", CAbi_BeforeStartDropsValue },
            { "c_abi_running_collector_stores_int_payload", CAbi_RunningCollectorStoresIntPayload },
            { "c_abi_duplicate_sensor_path_is_idempotent", CAbi_DuplicateSensorPathIsIdempotent },
            { "c_abi_long_comment_is_trimmed", CAbi_LongCommentIsTrimmed },
            { "c_abi_invalid_arguments_return_errors", CAbi_InvalidArgumentsReturnErrors },
            { "c_abi_missing_payload_returns_not_found", CAbi_MissingPayloadReturnsNotFound },
            { "cpp_wrapper_creates_sensor_and_reads_payload", CppWrapper_CreatesSensorAndReadsSentPayload },
            { "cpp_wrapper_reports_invalid_path", CppWrapper_ReportsInvalidPath },
            { "cpp_wrapper_start_twice_reports_invalid_state", CppWrapper_StartTwiceReportsInvalidState },
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

            test->second();
            return 0;
        }

        for (const auto& test : tests)
            test.second();
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }

    return 0;
}
