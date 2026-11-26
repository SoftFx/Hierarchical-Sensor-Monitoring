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
// With HSM_COLLECTOR_TABLES it also measures the table sensors' add path (#1848): add_row and
// add_rows of 100 rows, rows per second on one thread and on four (one sensor each). The bench needs no
// server, so it installs a sink channel through the test seam and a manual tick: the numbers are the
// cost of taking rows into the buffer, without packing.
//
// Output: a one-line JSON object to stdout, and to argv[1] if given.

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#if defined(HSM_COLLECTOR_TABLES)
#include "../src/tables/hsm_table_test_seams.hpp"
#endif

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

#if defined(HSM_COLLECTOR_TABLES)
    namespace tables = hsm::collector::tables;

    // Takes every registration and every block, keeps nothing.
    class SinkChannel : public tables::ITableChannel
    {
    public:
        std::size_t MaxBlockBytes() const override { return 4 * 1024 * 1024 - 40; }
        tables::TableBlockLimits Limits() const override { return tables::TableBlockLimits{}; }
        tables::TableRegistrationResult Register(const std::vector<tables::TableRegistration>& registrations) override
        {
            tables::TableRegistrationResult result;
            for (const auto& registration : registrations)
                result.accepted.insert(registration.path);
            return result;
        }
        tables::TableBlockAck Put(const tables::TableProducerId&, std::int64_t, std::vector<std::uint8_t>&) override
        {
            tables::TableBlockAck ack;
            ack.outcome = tables::TableBlockOutcome::Stored;
            return ack;
        }
        void Cancel() override {}
        void Reset() override {}
    };

    // Rows per second of `threads` writers, each on a sensor of its own, `rows` rows each, in calls of
    // `batch` rows (1: add_row).
    long long TableRowsPerSecond(int threads, long long rows, int batch)
    {
        hsm_collector_options_t options{};
        options.access_key = "bench-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "native-bench";
        options.computer_name = "bench-host";
        hsm_collector_t* collector = nullptr;
        if (hsm_collector_create(&options, &collector) != HSM_RESULT_OK)
            throw std::runtime_error("collector create failed");
        tables::test::InstallChannel(collector, std::make_shared<SinkChannel>());
        tables::test::SetManualTick(collector, true);
        hsm_table_options_t table_options{};
        table_options.struct_size = sizeof table_options;
        table_options.max_buffer_bytes = 8LL * 1024 * 1024 * 1024;
        hsm_collector_set_table_options(collector, &table_options);

        const hsm_table_column_t columns[] = { { "bid", HSM_TABLE_COLUMN_DOUBLE }, { "ask", HSM_TABLE_COLUMN_DOUBLE } };
        std::vector<hsm_table_sensor_t*> sensors(static_cast<std::size_t>(threads), nullptr);
        for (int t = 0; t < threads; ++t)
            hsm_collector_create_table_sensor(collector, ("bench/quotes/" + std::to_string(t)).c_str(), columns, 2, nullptr, &sensors[static_cast<std::size_t>(t)]);
        hsm_collector_start(collector);
        tables::test::RunTick(collector); // registers them

        std::vector<std::int64_t> times(static_cast<std::size_t>(batch));
        std::vector<hsm_table_cell_t> cells(static_cast<std::size_t>(batch) * 2);
        for (int i = 0; i < batch; ++i)
        {
            times[static_cast<std::size_t>(i)] = 1700000000000000000LL + i;
            cells[2 * static_cast<std::size_t>(i)].f64 = 1.0841;
            cells[2 * static_cast<std::size_t>(i) + 1].f64 = 1.0843;
        }

        const auto start = std::chrono::steady_clock::now();
        std::vector<std::thread> workers;
        for (int t = 0; t < threads; ++t)
            workers.emplace_back([&, t] {
                hsm_table_sensor_t* sensor = sensors[static_cast<std::size_t>(t)];
                for (long long done = 0; done < rows; done += batch)
                {
                    if (batch == 1)
                        hsm_table_sensor_add_row(sensor, times[0] + done, cells.data(), 2, nullptr);
                    else
                        hsm_table_sensor_add_rows(sensor, times.data(), cells.data(), nullptr, static_cast<std::size_t>(batch), nullptr);
                }
            });
        for (auto& worker : workers)
            worker.join();
        const auto end = std::chrono::steady_clock::now();

        std::uint64_t taken = 0;
        for (auto* sensor : sensors)
        {
            hsm_table_sensor_stats_t stats{};
            stats.struct_size = sizeof stats;
            hsm_table_sensor_get_stats(sensor, &stats);
            taken += stats.rows_accepted;
            hsm_table_sensor_release(sensor);
        }
        hsm_collector_destroy(collector);
        if (taken != static_cast<std::uint64_t>(rows) * static_cast<std::uint64_t>(threads))
            throw std::runtime_error("the bench dropped table rows");

        const double seconds = std::chrono::duration<double>(end - start).count();
        return seconds > 0.0 ? static_cast<long long>(static_cast<double>(rows) * threads / seconds) : 0;
    }
#endif

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
#if defined(HSM_COLLECTOR_TABLES)
        constexpr long long kTableRows = 2'000'000;
        json += ",\"table_add_row_rows_per_sec_1_thread\":" + std::to_string(TableRowsPerSecond(1, kTableRows, 1));
        json += ",\"table_add_row_rows_per_sec_4_threads\":" + std::to_string(TableRowsPerSecond(4, kTableRows, 1));
        json += ",\"table_add_rows_100_rows_per_sec_1_thread\":" + std::to_string(TableRowsPerSecond(1, kTableRows, 100));
        json += ",\"table_add_rows_100_rows_per_sec_4_threads\":" + std::to_string(TableRowsPerSecond(4, kTableRows, 100));
#endif
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
