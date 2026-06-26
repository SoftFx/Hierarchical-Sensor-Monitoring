// Native collector micro-benchmark (#1101) — enqueue throughput + peak RSS.
//
// A deliberately tiny "smoke" benchmark, NOT a hard CI gate: the scheduled
// native-collector-benchmark lane runs it, compares the result against
// bench/baseline.json, and only ALERTS on a regression (the perf story is a
// selling point of the port, so we track it over time rather than block PRs).
//
// It measures the hot producer path — Sensor::AddValue (validate + enqueue) —
// which is what an embedding application calls on every metric. Delivery is
// asynchronous and uses the in-memory recording sender (default build, no
// network), so the number reflects enqueue cost, not socket I/O.
//
// Output: a one-line JSON object to stdout, and to argv[1] if given.

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <windows.h>
#include <psapi.h>
#elif defined(__unix__) || defined(__APPLE__)
#include <sys/resource.h>
#endif

namespace
{

    // Peak resident set size in kilobytes, or 0 if the platform is not supported.
    long PeakRssKb()
    {
#if defined(_WIN32)
        PROCESS_MEMORY_COUNTERS pmc;
        if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc)))
            return static_cast<long>(pmc.PeakWorkingSetSize / 1024);
        return 0;
#elif defined(__unix__) || defined(__APPLE__)
        struct rusage usage;
        if (getrusage(RUSAGE_SELF, &usage) == 0)
        {
#if defined(__APPLE__)
            return static_cast<long>(usage.ru_maxrss / 1024); // macOS reports bytes
#else
            return static_cast<long>(usage.ru_maxrss); // Linux reports KB
#endif
        }
        return 0;
#else
        return 0;
#endif
    }

} // namespace

int main(int argc, char** argv)
{
    namespace hc = hsm::collector;

    // Bounded so a scheduled run finishes in seconds; large enough to be stable.
    constexpr int kSensorCount = 64;
    constexpr long long kTotalValues = 2'000'000;

    try
    {
        hc::CollectorOptions options;
        options.access_key = "bench-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "native-bench";
        options.computer_name = "bench-host";
        options.package_collect_period_ms = 50;
        options.max_queue_size = 200000;

        hc::Collector collector(options);

        std::vector<hc::IntSensor> sensors;
        sensors.reserve(kSensorCount);
        for (int i = 0; i < kSensorCount; ++i)
            sensors.push_back(collector.CreateIntSensor("bench/sensor/" + std::to_string(i)));

        collector.Start();

        // Warm up the queue/scheduler before timing.
        for (int i = 0; i < kSensorCount * 100; ++i)
            sensors[i % kSensorCount].AddValue(i);

        const auto start = std::chrono::steady_clock::now();
        for (long long i = 0; i < kTotalValues; ++i)
            sensors[i % kSensorCount].AddValue(static_cast<std::int32_t>(i));
        const auto end = std::chrono::steady_clock::now();

        collector.Stop();

        const double seconds = std::chrono::duration<double>(end - start).count();
        const long long opsPerSec =
            seconds > 0.0 ? static_cast<long long>(kTotalValues / seconds) : 0;
        const long rssKb = PeakRssKb();

        std::string json = "{";
        json += "\"enqueue_ops_per_sec\":" + std::to_string(opsPerSec) + ",";
        json += "\"values\":" + std::to_string(kTotalValues) + ",";
        json += "\"sensors\":" + std::to_string(kSensorCount) + ",";
        json += "\"elapsed_seconds\":" + std::to_string(seconds) + ",";
        json += "\"peak_rss_kb\":" + std::to_string(rssKb);
        json += "}";

        std::cout << json << '\n';

        if (argc > 1)
        {
            std::ofstream out(argv[1], std::ios::binary | std::ios::trunc);
            out << json << '\n';
        }

        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << "benchmark failed: " << ex.what() << '\n';
        return 1;
    }
}
