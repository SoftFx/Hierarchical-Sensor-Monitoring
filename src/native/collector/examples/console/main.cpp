// Native HSM collector console example (#1100) — the WrapperConsole equivalent for the public C++
// RAII API. Builds a collector, wires a logger + lifecycle listener via std::function, registers a
// few sensors (a custom double with a threshold alert, a rate sensor, a pull-function sensor) plus
// the built-in module sensors, starts, posts values, stops, and reports the counts.
//
// It links the header-only wrapper (hsm_collector::hsm_collector_cpp). With the default build (no
// HTTP transport) the core uses the in-memory recording sender, so the example runs end-to-end with
// no server — which is what makes it a CI smoke check.

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>

int main()
{
    namespace hc = hsm::collector;

    try
    {
        hc::CollectorOptions options;
        options.access_key = "example-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "native-example";
        options.computer_name = "example-host";
        options.package_collect_period_ms = 50; // dispatch quickly so the example finishes fast

        hc::Collector collector(options);

        collector.SetLogger([](hc::LogLevel level, const std::string& message) {
            std::cerr << "[hsm] (" << static_cast<int>(level) << ") " << message << '\n';
        });
        collector.AddLifecycleListener([](hc::CollectorStatus status) {
            std::cout << "[hsm] status -> " << static_cast<int>(status) << '\n';
        });

        // Built-in module sensors (collector alive/version/errors + queue self-diagnostics).
        collector.AddAllModuleSensors("1.0.0.0");

        // A custom double sensor with a "value over 100" threshold alert.
        auto alert = collector.CreateAlert(hc::AlertKind::Instant)
                         .If(hc::AlertProperty::Value, hc::AlertOperation::GreaterThan, "100")
                         .ThenNotify("[$product]$path $operation $target")
                         .WithIcon(hc::AlertIcon::Warning);
        auto temperature = collector.CreateDoubleSensor("Example/Temperature");
        temperature.AttachAlert(alert);

        // A rate sensor and a pull-function sensor driven by a std::function.
        auto requests = collector.CreateRateSensor("Example/Requests");
        int tick = 0;
        [[maybe_unused]] auto uptime = collector.CreateFunctionSensor(
            "Example/UptimeTicks",
            std::chrono::seconds(1),
            [&tick]() -> std::int32_t { return ++tick; });

        collector.Start();

        for (int i = 0; i < 5; ++i)
        {
            temperature.AddValue(90.0 + i * 5.0);
            requests.AddValue(1.0);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(200));
        collector.Stop();

        std::cout << "[hsm] registrations=" << collector.RegistrationCount()
                  << " sent=" << collector.SentCount() << '\n';
        return 0;
    }
    catch (const hc::Error& ex)
    {
        std::cerr << "[hsm] error: " << ex.what() << '\n';
        return 1;
    }
}
