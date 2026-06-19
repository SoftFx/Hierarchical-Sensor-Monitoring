// Minimal consumer exercising the hsm-collector package purely through find_package(hsm_collector).
// With the default (no-HTTP) build the core uses the in-memory recording sender, so this runs with
// no server and is a clean-consumer smoke check.

#include <hsm_collector/hsm_collector.hpp>

#include <iostream>

int main()
{
    namespace hc = hsm::collector;

    hc::CollectorOptions options;
    options.access_key = "package-test-key";
    options.server_address = "https://localhost";
    options.port = 443;
    options.module = "conan-test";
    options.package_collect_period_ms = 50;

    hc::Collector collector(options);
    auto sensor = collector.CreateIntSensor("TestPackage/Value");

    collector.Start();
    sensor.AddValue(42);
    collector.Stop();

    std::cout << "hsm-collector package consumed OK; sent=" << collector.SentCount() << '\n';
    return 0;
}
