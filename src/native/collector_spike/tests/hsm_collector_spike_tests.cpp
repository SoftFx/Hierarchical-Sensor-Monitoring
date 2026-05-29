#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"

#include <iostream>
#include <stdexcept>
#include <string>

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

    void CAbi_AddingValueBeforeStartDropsValueLikeManagedCollector()
    {
        hsm_collector_t* collector = nullptr;
        hsm_sensor_t* sensor = nullptr;
        auto options = TestOptions();

        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create failed");
        Require(hsm_collector_create_int_sensor(collector, "spike/int", &sensor) == HSM_RESULT_OK, "sensor create failed");
        Require(hsm_sensor_add_int(sensor, 7, HSM_SENSOR_STATUS_OK, "before start") == HSM_RESULT_OK, "add before start failed");
        Require(hsm_collector_sent_count(collector) == 0, "stopped collector should drop values");

        hsm_sensor_release(sensor);
        hsm_collector_destroy(collector);
    }

    void CAbi_RunningCollectorStoresIntValuePayload()
    {
        hsm_collector_t* collector = nullptr;
        hsm_sensor_t* sensor = nullptr;
        auto options = TestOptions();

        Require(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create failed");
        Require(hsm_collector_create_int_sensor(collector, "spike/int", &sensor) == HSM_RESULT_OK, "sensor create failed");
        Require(hsm_collector_start(collector) == HSM_RESULT_OK, "collector start failed");
        Require(hsm_sensor_add_int(sensor, 42, HSM_SENSOR_STATUS_WARNING, "watch") == HSM_RESULT_OK, "add value failed");
        Require(hsm_collector_sent_count(collector) == 1, "running collector should store one payload");

        const char* json = nullptr;
        Require(hsm_collector_get_sent_json(collector, 0, &json) == HSM_RESULT_OK, "payload lookup failed");

        const std::string payload{ json };
        Contains(payload, "\"Type\":1");
        Contains(payload, "\"Path\":\"spike/int\"");
        Contains(payload, "\"Value\":42");
        Contains(payload, "\"Status\":2");
        Contains(payload, "\"Comment\":\"watch\"");
        Contains(payload, "\"UnixTimeMs\":");

        hsm_sensor_release(sensor);
        hsm_collector_destroy(collector);
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
}

int main()
{
    try
    {
        CAbi_AddingValueBeforeStartDropsValueLikeManagedCollector();
        CAbi_RunningCollectorStoresIntValuePayload();
        CppWrapper_CreatesSensorAndReadsSentPayload();
        CppWrapper_ReportsInvalidPath();
    }
    catch (const std::exception& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }

    return 0;
}
