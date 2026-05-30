#include "hsm_collector/hsm_collector.h"
#include <iostream>
#include <fstream>
#include <functional>
#include <map>
#include <mutex>
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

    std::string ExpandTextToken(const std::string& value)
    {
        const std::string repeat_prefix = "repeat:";
        if (value.rfind(repeat_prefix, 0) != 0)
        {
            if (value == "token:json-special")
                return "quote\"slash\\tab\tnewline\n";

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

        if (action == "create_bool_sensor")
        {
            Require(step.size() >= 2, "create_bool_sensor requires path");
            state.sensors.push_back(CreateBoolSensor(state.collector.value, step[1].c_str()));
            return;
        }

        if (action == "create_double_sensor")
        {
            Require(step.size() >= 2, "create_double_sensor requires path");
            state.sensors.push_back(CreateDoubleSensor(state.collector.value, step[1].c_str()));
            return;
        }

        if (action == "create_string_sensor")
        {
            Require(step.size() >= 2, "create_string_sensor requires path");
            state.sensors.push_back(CreateStringSensor(state.collector.value, step[1].c_str()));
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

    const std::map<std::string, std::function<void(const std::string&)>>& Tests()
    {
        static const std::map<std::string, std::function<void(const std::string&)>> tests = {
            { "conformance_instant_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_lifecycle_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_stress_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_value_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_cardinality_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_instant_mixed_contract", [](const std::string& path) { RunConformanceContract(path); } },
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
