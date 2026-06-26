// Native HSM collector -> real server, Windows live default sensors (#1164). Registers the standard
// computer + module default sensors (Total CPU, Free RAM, disks, network, collector self-sensors) and
// streams their LIVE values, read via the Windows PDH/Win32 metric-source factory, to a running HSM
// server. Built only with HSM_COLLECTOR_HTTP=ON on Windows.
//
// Usage: hsm_windows_monitor [server_address] [port] [access_key] [seconds]
//   defaults: https://localhost 44330 demo-key 30

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
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
    const int seconds = argc > 4 ? std::atoi(argv[4]) : 30;

    try
    {
        hc::CollectorOptions options;
        options.access_key = access_key;
        options.server_address = server;
        options.port = port;
        options.module = "native-windows-monitor";
        options.computer_name = "demo-host";
        options.allow_untrusted_server_certificate = true; // self-signed Docker server
        options.package_collect_period_ms = 1000;

        hc::Collector collector(options);
        collector.SetLogger([](hc::LogLevel level, const std::string& message) {
            std::cerr << "[hsm] (" << static_cast<int>(level) << ") " << message << '\n';
        });

        // Real server transport (#1165) + the Windows live readers (#1164). Install both before Start.
        collector.UseHttpTransport();
        collector.InstallWindowsMetricSources();

        // The standard host + module default-sensor catalog (registration parity with .NET); the
        // value-typed ones (Total CPU, Free RAM, disk gauges, free disk, network) now read live.
        collector.AddAllComputerSensors();
        collector.AddAllModuleSensors("1.0.0.0");

        std::cout << "Monitoring Windows -> " << server << ':' << port << " for " << seconds << "s...\n";
        collector.Start();
        std::this_thread::sleep_for(std::chrono::seconds(seconds));
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
