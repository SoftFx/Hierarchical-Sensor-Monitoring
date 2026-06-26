// Native HSM collector -> real server example (#1165). Unlike the console example (which uses the
// in-memory recording sender), this one calls UseHttpTransport() and actually POSTs to a running
// HSM server: it registers the sensors on /commands at Start and streams live values to /list in the
// .NET server wire format. Point it at a local Dockerized server to watch the values appear in the
// product tree.
//
// Built only in the HTTP configuration (HSM_COLLECTOR_HTTP=ON), which links libcurl.
//
// Usage: hsm_server_monitor [server_address] [port] [access_key] [seconds]
//   defaults: https://localhost 44330 demo-key 15
//   e.g. hsm_server_monitor https://localhost 44330 <your-access-key> 30

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>

int main(int argc, char** argv)
{
    namespace hc = hsm::collector;

    const std::string server = argc > 1 ? argv[1] : "https://localhost";
    const int port = argc > 2 ? std::atoi(argv[2]) : 44330;
    const std::string access_key = argc > 3 ? argv[3] : "demo-key";
    const int seconds = argc > 4 ? std::atoi(argv[4]) : 15;

    try
    {
        hc::CollectorOptions options;
        options.access_key = access_key;
        options.server_address = server;
        options.port = port;
        options.module = "native-server-monitor";
        options.computer_name = "demo-host";
        // A local Dockerized server uses a self-signed certificate; accept it for the demo.
        options.allow_untrusted_server_certificate = true;
        options.package_collect_period_ms = 1000; // flush every second so values appear promptly

        hc::Collector collector(options);
        collector.SetLogger([](hc::LogLevel level, const std::string& message) {
            std::cerr << "[hsm] (" << static_cast<int>(level) << ") " << message << '\n';
        });

        // The foundation (#1165): switch from the in-memory recorder to the real server transport.
        // Registrations POST to /commands at Start; values stream to /list in wire format.
        collector.UseHttpTransport();

        auto counter = collector.CreateIntSensor("Demo/Counter");
        auto wave = collector.CreateDoubleSensor("Demo/Wave");

        std::cout << "Registering + streaming to " << server << ':' << port << " for " << seconds << "s...\n";
        collector.Start();

        for (int i = 0; i < seconds; ++i)
        {
            counter.AddValue(i);
            wave.AddValue(std::sin(i / 3.0) * 100.0);
            std::this_thread::sleep_for(std::chrono::seconds(1));
        }

        collector.Stop();
        std::cout << "done. registrations=" << collector.RegistrationCount()
                  << " sent=" << collector.SentCount() << '\n';
        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << "error: " << ex.what() << '\n';
        return 1;
    }
}
