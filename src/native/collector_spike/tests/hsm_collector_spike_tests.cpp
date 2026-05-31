#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"
#include <iostream>
#include <fstream>
#include <cstdint>
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

        struct MixedInstantSet
        {
            size_t bool_index;
            size_t int_index;
            size_t double_index;
            size_t string_index;
            size_t enum_index;
        };

        std::vector<MixedInstantSet> mixed_sets;
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

        if (action == "create_enum_sensor")
        {
            Require(step.size() >= 2, "create_enum_sensor requires path");
            state.sensors.push_back(CreateEnumSensor(state.collector.value, step[1].c_str()));
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

    const std::map<std::string, std::function<void(const std::string&)>>& Tests()
    {
        static const std::map<std::string, std::function<void(const std::string&)>> tests = {
            { "native_invalid_argument_clears_out_params", [](const std::string&) { NativeInvalidArgumentClearsOutParams(); } },
            { "native_add_after_collector_destroy_is_rejected", [](const std::string&) { NativeAddAfterCollectorDestroyIsRejected(); } },
            { "native_sent_json_failure_reports_fresh_error", [](const std::string&) { NativeSentJsonFailureReportsFreshError(); } },
            { "native_wrapper_sent_json_missing_throws_message", [](const std::string&) { NativeWrapperSentJsonMissingThrowsMessage(); } },
            { "conformance_instant_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_lifecycle_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_stress_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_stress_mixed_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_value_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_cardinality_int_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_instant_mixed_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_last_value_contract", [](const std::string& path) { RunConformanceContract(path); } },
            { "conformance_enum_contract", [](const std::string& path) { RunConformanceContract(path); } },
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
