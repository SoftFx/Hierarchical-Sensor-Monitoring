// Table sensors of the native collector (#1848): the C ABI, the C++ wrapper, the pipeline's parts, its
// lifetime under threads, and the table verbs of the conformance driver
// (tests/conformance/collector/table_sensor_contract.hsmtest). Compiled in every build; what needs the
// block writer or the test block reader (libzstd) is compiled with HSM_COLLECTOR_TABLES only.
//
// No test decides by time: ticks run through the manual-tick seam or a manual clock, and a wait only
// fails a test when it expires.

#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"

#include "../src/tables/hsm_table_pipeline.hpp"
#include "../src/tables/hsm_table_test_seams.hpp"

#if defined(HSM_COLLECTOR_TABLES)
#include "table_block_test_reader.hpp"
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <set>
#include <stdexcept>
#include <string>
#include <system_error>
#include <thread>
#include <type_traits>
#include <vector>

extern "C" void hsm_collector_test_install_manual_clock(hsm_collector_t* collector, int64_t base_ms);
extern "C" void hsm_collector_test_advance_clock_ms(hsm_collector_t* collector, int64_t delta_ms);

namespace tables = hsm::collector::tables;

namespace
{
    void Check(bool condition, const std::string& message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

    template <typename T>
    std::string Text(const T& value)
    {
        if constexpr (std::is_same_v<T, std::string>)
            return "'" + value + "'";
        else
            return std::to_string(value);
    }

    template <typename A, typename B>
    void CheckEqual(const A& expected, const B& actual, const std::string& what)
    {
        if (!(expected == actual))
            throw std::runtime_error(what + ": expected " + Text(expected) + ", actual " + Text(actual));
    }

    // A wait that only fails a test when it expires; it never decides the outcome.
    template <typename Predicate>
    void WaitOrFail(Predicate predicate, const std::string& what, int seconds = 60)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
        while (!predicate())
        {
            if (std::chrono::steady_clock::now() > deadline)
                throw std::runtime_error("timed out waiting for " + what);
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    }

    hsm_collector_options_t Options()
    {
        hsm_collector_options_t options{};
        options.access_key = "test-key";
        options.server_address = "https://localhost";
        options.port = 443;
        options.module = "conformance-module";
        options.computer_name = "conformance-host";
        options.max_values_in_package = 50;
        options.package_collect_period_ms = 20;
        return options;
    }

    struct Collector
    {
        hsm_collector_t* value = nullptr;

        explicit Collector(const hsm_collector_options_t& options = Options())
        {
            Check(hsm_collector_create(&options, &value) == HSM_RESULT_OK, "collector create failed");
        }
        Collector(const Collector&) = delete;
        Collector& operator=(const Collector&) = delete;
        ~Collector() { Destroy(); }
        void Destroy()
        {
            hsm_collector_destroy(value);
            value = nullptr;
        }
    };

    struct Table
    {
        hsm_table_sensor_t* value = nullptr;
        Table() = default;
        explicit Table(hsm_table_sensor_t* handle)
            : value(handle)
        {
        }
        Table(const Table&) = delete;
        Table& operator=(const Table&) = delete;
        Table(Table&& other) noexcept
            : value(std::exchange(other.value, nullptr))
        {
        }
        ~Table() { hsm_table_sensor_release(value); }
    };

    Table CreateTable(hsm_collector_t* collector, const std::string& path, const std::vector<hsm_table_column_t>& columns, int location = 0)
    {
        hsm_table_sensor_options_t options = hsm_table_sensor_options_default();
        options.sensor_location = location;
        hsm_table_sensor_t* sensor = nullptr;
        const auto result = hsm_collector_create_table_sensor(collector, path.c_str(), columns.data(), columns.size(), &options, &sensor);
        Check(result == HSM_RESULT_OK, "create_table_sensor " + path + " failed: " + std::to_string(static_cast<int>(result)) + " " +
                                           (hsm_collector_last_error(collector) ? hsm_collector_last_error(collector) : ""));
        return Table(sensor);
    }

    hsm_table_sensor_stats_t Stats(const Table& table)
    {
        hsm_table_sensor_stats_t stats{};
        stats.struct_size = sizeof stats;
        Check(hsm_table_sensor_get_stats(table.value, &stats) == HSM_RESULT_OK, "get_stats failed");
        return stats;
    }

    std::string Reason(const Table& table)
    {
        char buffer[512];
        hsm_table_sensor_state_reason(table.value, buffer, sizeof buffer);
        return buffer;
    }

    hsm_result_t AddDouble(const Table& table, int64_t time_ns, double value)
    {
        hsm_table_cell_t cell;
        cell.f64 = value;
        return hsm_table_sensor_add_row(table.value, time_ns, &cell, 1, nullptr);
    }

    // ---- Parts ---------------------------------------------------------------------------------------

    void PathNormalization()
    {
        CheckEqual(std::string("a/b"), tables::NormalizeTablePath("a//b"), "empty segment");
        CheckEqual(std::string("a/b c/d"), tables::NormalizeTablePath(" /a/ b c /d/ "), "trimmed segments");
        CheckEqual(std::string(""), tables::NormalizeTablePath("//  / "), "nothing left");
        // .NET char.IsWhiteSpace: NBSP, U+2003 and U+3000 are trimmed; U+200B (zero width space) is not.
        CheckEqual(std::string("x/y"), tables::NormalizeTablePath("\xC2\xA0x\xE2\x80\x83/\xE3\x80\x80y"), "unicode white space");
        CheckEqual(std::string("\xE2\x80\x8Bx"), tables::NormalizeTablePath("\xE2\x80\x8Bx"), "zero width space kept");
        std::string error;
        Check(!tables::CheckTablePath("", "/", 1024, error), "an empty path passes");
        Check(!tables::CheckTablePath("a\\b", "a\\b", 1024, error), "a backslash passes");
        Check(!tables::CheckTablePath(std::string(1025, 'x'), "x", 1024, error), "1025 bytes pass");
        Check(tables::CheckTablePath(std::string(1024, 'x'), "x", 1024, error), "1024 bytes are refused");
    }

    void TimeConversion()
    {
        CheckEqual(static_cast<std::uint64_t>(tables::kUnixEpochTicks), tables::TicksFromUnixNanoseconds(0), "epoch");
        CheckEqual(static_cast<std::uint64_t>(tables::kUnixEpochTicks + 1), tables::TicksFromUnixNanoseconds(199), "rounded down");
        CheckEqual(static_cast<std::uint64_t>(tables::kUnixEpochTicks - 2), tables::TicksFromUnixNanoseconds(-150), "rounded down before 1970");
        Check(tables::TicksFromUnixNanoseconds((std::numeric_limits<int64_t>::max)()) <= tables::TableBlockFormat::kMaxTimeTicks, "int64 max out of range");
        Check(tables::TicksFromUnixNanoseconds((std::numeric_limits<int64_t>::min)()) > 0, "int64 min out of range");
    }

    void BufferGrowthAndSwap()
    {
        tables::TableRowBuffer buffer(2);
        CheckEqual(std::size_t{ 0 }, buffer.Capacity(), "no array before the first row");
        std::vector<int64_t> times(300);
        std::vector<std::uint64_t> cells(600);
        for (std::size_t i = 0; i < 300; ++i)
        {
            times[i] = static_cast<int64_t>(i) * 100;
            cells[2 * i] = i;
            cells[2 * i + 1] = 1000 + i;
        }
        buffer.Append(times.data(), cells.data(), nullptr, 1);
        CheckEqual(tables::TableRowBuffer::kInitialCapacity, buffer.Capacity(), "initial capacity");
        buffer.Append(times.data() + 1, cells.data() + 2, nullptr, 299);
        CheckEqual(std::size_t{ 300 }, buffer.Count(), "rows");
        CheckEqual(std::size_t{ 512 }, buffer.Capacity(), "doubled");
        for (std::size_t i = 0; i < 300; ++i)
        {
            CheckEqual(static_cast<std::uint64_t>(tables::kUnixEpochTicks + static_cast<int64_t>(i)), buffer.Times()[i], "time " + std::to_string(i));
            CheckEqual(static_cast<std::uint64_t>(1000 + i), buffer.Values(1)[i], "value " + std::to_string(i));
        }
        Check(!buffer.HasNulls(0) && !buffer.HasNulls(1), "a null map without a null");
        buffer.Clear();
        CheckEqual(std::size_t{ 0 }, buffer.Capacity(), "a buffer past 256 rows gives its arrays up");
        buffer.Append(times.data(), cells.data(), nullptr, 10);
        buffer.Clear();
        CheckEqual(tables::TableRowBuffer::kInitialCapacity, buffer.Capacity(), "a small buffer keeps its arrays");
    }

    void BufferFailedGrowth()
    {
        tables::TableRowBuffer buffer(1);
        std::vector<int64_t> times(100, 0);
        std::vector<std::uint64_t> cells(100, 7);
        buffer.Append(times.data(), cells.data(), nullptr, 64);
        buffer.before_growth = [] { throw std::bad_alloc(); };
        bool threw = false;
        try
        {
            buffer.Append(times.data(), cells.data(), nullptr, 1);
        }
        catch (const std::bad_alloc&)
        {
            threw = true;
        }
        Check(threw, "the growth did not fail");
        CheckEqual(std::size_t{ 64 }, buffer.Count(), "rows after a failed growth");
        CheckEqual(std::size_t{ 64 }, buffer.Capacity(), "arrays after a failed growth");
        // A first null map is a growth too, and fails the same way.
        std::uint8_t null_bit = 1;
        threw = false;
        try
        {
            tables::TableRowBuffer small(1);
            small.Append(times.data(), cells.data(), nullptr, 1);
            small.before_growth = [] { throw std::bad_alloc(); };
            small.Append(times.data(), cells.data(), &null_bit, 1);
        }
        catch (const std::bad_alloc&)
        {
            threw = true;
        }
        Check(threw, "the first null map did not fail");

        // Through the sensor: the rows of the call are dropped and counted, the reservation goes back.
        auto shared = std::make_shared<tables::TableShared>(1 << 20);
        shared->accepting = true;
        shared->ledger_open = true;
        tables::TableSchema schema;
        std::string error;
        Check(tables::TableSchema::Create({ { "time", tables::TableColumnType::Time }, { "v", tables::TableColumnType::Double } }, 64, schema, error), error);
        tables::TableSensor sensor(shared, "p", { { "v", tables::TableColumnType::Double } }, schema, "", 0);
        sensor.SetBeforeGrowthForTests([] { throw std::bad_alloc(); });
        hsm_table_cell_t cell;
        cell.f64 = 1.5;
        int64_t time = 0;
        CheckEqual(static_cast<int>(HSM_RESULT_LIMIT_EXCEEDED), static_cast<int>(sensor.Add(&time, &cell, nullptr, 1)), "result of a failed growth");
        CheckEqual(std::uint64_t{ 1 }, sensor.RowsDropped(), "dropped");
        CheckEqual(std::int64_t{ 0 }, shared->reserved.load(), "reservation given back");
        CheckEqual(std::int64_t{ 1 }, shared->ledger.Total(tables::TableDropReason::BufferGrowthFailed), "ledger");
    }

    void BufferNullMapOnFirstUse()
    {
        tables::TableRowBuffer buffer(3);
        std::vector<int64_t> times(4, 0);
        std::vector<std::uint64_t> cells(12, 5);
        buffer.Append(times.data(), cells.data(), nullptr, 2);
        Check(!buffer.HasNulls(0) && !buffer.HasNulls(1) && !buffer.HasNulls(2), "maps before a null");
        // The call's row 0, column 1 has no value: bit 0 * 3 + 1. It lands in the buffer's row 2.
        std::uint8_t nulls = 0x02;
        buffer.Append(times.data(), cells.data(), &nulls, 1);
        Check(!buffer.HasNulls(0) && buffer.HasNulls(1) && !buffer.HasNulls(2), "only column 1 gets a map");
        Check(buffer.IsNull(1, 2) && !buffer.IsNull(1, 0) && !buffer.IsNull(1, 1), "the null cell");
        CheckEqual(std::uint64_t{ 0 }, buffer.Values(1)[2], "a null cell is zero");
        std::uint8_t map = 0;
        Check(buffer.CopyNullMap(1, 1, 2, &map), "slice has a null");
        CheckEqual(std::uint8_t{ 2 }, map, "slice map shifted");
        buffer.Clear();
        Check(!buffer.HasNulls(1), "map flag after clear");
        buffer.Append(times.data(), cells.data(), nullptr, 3);
        Check(!buffer.IsNull(1, 2), "a stale bit after clear");
    }

    void BoundNeverOvershoots()
    {
        tables::TableShared shared(100);
        Check(!shared.TryReserve(101), "a request above the bound");
        CheckEqual(std::int64_t{ 0 }, shared.reserved.load(), "a refused request touched the counter");
        Check(shared.TryReserve(60), "60 of 100");
        Check(!shared.TryReserve(41), "101 of 100");
        Check(shared.TryReserve(40), "100 of 100");
        shared.Release(100);

        // Many threads: the counter never passes the bound, and every reservation is given back.
        tables::TableShared bounded(1000);
        std::atomic<std::int64_t> peak{ 0 };
        std::vector<std::thread> threads;
        for (int t = 0; t < 8; ++t)
            threads.emplace_back([&] {
                for (int i = 0; i < 20000; ++i)
                {
                    if (bounded.TryReserve(24))
                    {
                        const auto now = bounded.reserved.load();
                        std::int64_t seen = peak.load();
                        while (now > seen && !peak.compare_exchange_weak(seen, now))
                        {
                        }
                        bounded.Release(24);
                    }
                }
            });
        for (auto& thread : threads)
            thread.join();
        Check(peak.load() <= 1000, "the counter passed the bound: " + std::to_string(peak.load()));
        CheckEqual(std::int64_t{ 0 }, bounded.reserved.load(), "reservations left");
    }

    void LedgerCountsByReason()
    {
        tables::TableLedger ledger;
        ledger.Add(3, tables::TableDropReason::BufferFull);
        ledger.Add(2, tables::TableDropReason::BufferFull);
        ledger.Add(1, tables::TableDropReason::NotRunning);
        ledger.Add(0, tables::TableDropReason::BacklogFull);
        const auto reasons = ledger.TakeReasons();
        CheckEqual(std::size_t{ 2 }, reasons.size(), "reasons");
        Check(ledger.TakeReasons().empty(), "reasons are taken once");
        CheckEqual(std::int64_t{ 6 }, ledger.TakeUnreported(), "unreported");
        CheckEqual(std::int64_t{ 0 }, ledger.TakeUnreported(), "taken");
        ledger.GiveBack(4);
        CheckEqual(std::int64_t{ 4 }, ledger.TakeUnreported(), "given back");
        CheckEqual(std::int64_t{ 5 }, ledger.Total(tables::TableDropReason::BufferFull), "total");
    }

    std::shared_ptr<tables::PendingBlock> Block(std::int64_t sequence, std::size_t bytes, const std::shared_ptr<tables::TableSensor>& sensor)
    {
        auto block = std::make_shared<tables::PendingBlock>();
        block->sequence = sequence;
        block->frame.assign(tables::kTableFrameHeaderBytes + bytes, 0);
        block->tables.push_back({ sensor, 1 });
        block->rows = 1;
        return block;
    }

    void BacklogKeepsBlockInFlight()
    {
        auto shared = std::make_shared<tables::TableShared>(1 << 20);
        shared->ledger_open = true;
        tables::TableSchema schema;
        std::string error;
        Check(tables::TableSchema::Create({ { "time", tables::TableColumnType::Time }, { "v", tables::TableColumnType::Double } }, 64, schema, error), error);
        auto sensor = std::make_shared<tables::TableSensor>(shared, "p", std::vector<tables::TableRegistrationColumn>{ { "v", tables::TableColumnType::Double } }, schema, "", 0);

        tables::TableBacklog backlog(250);
        CheckEqual(std::size_t{ 0 }, backlog.Append(Block(0, 100, sensor)), "first");
        const auto head = backlog.BeginHead();
        CheckEqual(std::int64_t{ 0 }, head->sequence, "head");
        CheckEqual(std::size_t{ 0 }, backlog.Append(Block(1, 100, sensor)), "second");
        // 300 bytes: the oldest that is not in flight goes, leaving a gap, and its rows are counted.
        CheckEqual(std::size_t{ 1 }, backlog.Append(Block(2, 100, sensor)), "dropped");
        CheckEqual(std::uint64_t{ 1 }, sensor->RowsDropped(), "dropped rows counted");
        CheckEqual(std::int64_t{ 1 }, shared->ledger.Total(tables::TableDropReason::BacklogFull), "in the ledger as backlog full");
        const auto sequences = backlog.Sequences();
        Check(sequences == std::vector<std::int64_t>{ 0, 2 }, "sequences after the drop: the one after the head went");
        // Not delivered: stays at the head.
        backlog.EndFlight(head);
        CheckEqual(std::int64_t{ 0 }, backlog.BeginHead()->sequence, "head again");
        Check(backlog.Settle(head), "settle");
        Check(!backlog.Settle(head), "settled twice");
        CheckEqual(std::int64_t{ 100 }, backlog.Bytes(), "bytes");
        // With nothing in flight, the bound drops the head too.
        backlog.EndFlight(head);
        CheckEqual(std::size_t{ 1 }, backlog.Append(Block(3, 200, sensor)), "head dropped when idle");
        Check(backlog.Sequences() == std::vector<std::int64_t>{ 3 }, "the oldest went");
    }

    // ---- The C ABI in every build --------------------------------------------------------------------

    void AbiRejectsWrongShapes()
    {
        Collector collector;
        const hsm_table_column_t bid{ "bid", HSM_TABLE_COLUMN_DOUBLE };
        hsm_table_sensor_t* sensor = reinterpret_cast<hsm_table_sensor_t*>(1);
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &bid, 0, nullptr, &sensor)), "no column");
        Check(sensor == nullptr, "out not cleared");
        const hsm_table_column_t time_column{ "time", HSM_TABLE_COLUMN_DOUBLE };
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &time_column, 1, nullptr, &sensor)), "time");
        const hsm_table_column_t twice[] = { { "a", HSM_TABLE_COLUMN_DOUBLE }, { "a", HSM_TABLE_COLUMN_INT64 } };
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", twice, 2, nullptr, &sensor)), "duplicate");
        const hsm_table_column_t bad_type{ "x", static_cast<hsm_table_column_type_t>(1) };
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &bad_type, 1, nullptr, &sensor)), "type");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, nullptr, &bid, 1, nullptr, &sensor)), "null path");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "a\\b", &bid, 1, nullptr, &sensor)), "backslash");
        hsm_table_sensor_options_t options = hsm_table_sensor_options_default();
        options.sensor_location = 7;
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &bid, 1, &options, &sensor)), "location");
        options.struct_size = 0;
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &bid, 1, &options, &sensor)), "struct_size 0");
        Check(std::string(hsm_collector_last_error(collector.value)).find("struct_size") != std::string::npos, "last error names the field");
        Check(tables::test::Pipeline(collector.value) == nullptr, "a refused create made the pipeline");

        // An old caller's shorter options struct: only description is read, the rest are defaults.
        struct OldOptions
        {
            size_t struct_size;
            const char* description;
        } old{ sizeof(OldOptions), "old" };
        Table table(nullptr);
        CheckEqual(static_cast<int>(HSM_RESULT_OK),
                   static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &bid, 1, reinterpret_cast<const hsm_table_sensor_options_t*>(&old), &table.value)),
                   "short options");

        // The same path and columns: a handle to the same sensor; other columns: refused.
        Table again = CreateTable(collector.value, "t", { bid });
        CheckEqual(std::string(hsm_table_sensor_path(table.value)), std::string(hsm_table_sensor_path(again.value)), "same sensor");
        const hsm_table_column_t other{ "bid", HSM_TABLE_COLUMN_INT64 };
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "t", &other, 1, nullptr, &sensor)), "other columns");

        // Shapes of the add calls: refused, not counted.
        hsm_table_cell_t cells[2];
        cells[0].f64 = 1;
        cells[1].f64 = 2;
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_row(table.value, 0, cells, 2, nullptr)), "two cells");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_row(table.value, 0, nullptr, 1, nullptr)), "null cells");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_row(nullptr, 0, cells, 1, nullptr)), "null sensor");
        size_t taken = 99;
        CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_table_sensor_add_rows(table.value, nullptr, nullptr, nullptr, 0, &taken)), "zero rows");
        CheckEqual(size_t{ 0 }, taken, "zero rows taken");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_rows(table.value, nullptr, cells, nullptr, 1, &taken)), "null times");
        // A row count no array can have (SIZE_MAX: an n - 1 below zero) is a wrong shape too.
        std::int64_t time = 0;
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_rows(table.value, &time, cells, nullptr, SIZE_MAX, &taken)), "SIZE_MAX rows");
        CheckEqual(std::uint64_t{ 0 }, Stats(table).rows_dropped, "a wrong shape counted");

        // After dispose: a row is refused (and counted on the sensor), a create is refused.
        hsm_collector_dispose(collector.value);
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(hsm_table_sensor_add_row(table.value, 0, cells, 1, nullptr)), "after dispose");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(hsm_collector_create_table_sensor(collector.value, "u", &bid, 1, nullptr, &sensor)), "create after dispose");

        // Stats of a short struct: nothing written past struct_size.
        struct ShortStats
        {
            size_t struct_size;
            hsm_table_sensor_state_t state;
            std::uint64_t guard;
        } short_stats{ offsetof(hsm_table_sensor_stats_t, rows_accepted), HSM_TABLE_SENSOR_REGISTERED, 0xABCDEF };
        Check(hsm_table_sensor_get_stats(table.value, reinterpret_cast<hsm_table_sensor_stats_t*>(&short_stats)) == HSM_RESULT_OK, "short stats");
        CheckEqual(std::uint64_t{ 0xABCDEF }, short_stats.guard, "wrote past struct_size");

        // Table options: after the first table sensor they are refused.
        hsm_table_options_t table_options{};
        table_options.struct_size = sizeof table_options;
        Collector fresh;
        table_options.tick_period_ms = 999;
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_collector_set_table_options(fresh.value, &table_options)), "tick below 1 s");
        table_options.tick_period_ms = 2000;
        CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_set_table_options(fresh.value, &table_options)), "options");
        Table first = CreateTable(fresh.value, "x", { bid });
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(hsm_collector_set_table_options(fresh.value, &table_options)), "options after a sensor");
    }

    // Without a channel a sensor is Unavailable and says why; its rows are dropped and counted, and the
    // losses of the run are reported (logger and the stop line).
    void UnavailableSaysWhy()
    {
        Collector collector;
        std::vector<std::string> logged;
        std::mutex log_mutex;
        struct Log
        {
            std::vector<std::string>* lines;
            std::mutex* mutex;
        } log{ &logged, &log_mutex };
        hsm_collector_set_logger(
            collector.value,
            [](hsm_log_level_t, const char* message, void* user) {
                auto* target = static_cast<Log*>(user);
                std::lock_guard<std::mutex> guard(*target->mutex);
                target->lines->push_back(message);
            },
            &log);

        Table table = CreateTable(collector.value, "quotes", { { "bid", HSM_TABLE_COLUMN_DOUBLE } });
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(table).state), "state");
#if defined(HSM_COLLECTOR_TABLES)
        Check(Reason(table).find("session transport") != std::string::npos, "reason: " + Reason(table));
#else
        Check(Reason(table).find("HSM_COLLECTOR_TABLES=OFF") != std::string::npos, "reason: " + Reason(table));
#endif
        Check(hsm_table_sensor_state_reason(table.value, nullptr, 0) == Reason(table).size(), "needed length");
        char small[5];
        hsm_table_sensor_state_reason(table.value, small, sizeof small);
        CheckEqual(std::size_t{ 4 }, std::strlen(small), "cut to the buffer");

        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(AddDouble(table, 0, 1.0)), "add to an unavailable sensor");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, "dropped");
        CheckEqual(std::uint64_t{ 0 }, Stats(table).rows_accepted, "accepted");

        std::lock_guard<std::mutex> guard(log_mutex);
        bool unavailable = false;
        bool stop_line = false;
        for (const auto& line : logged)
        {
            unavailable = unavailable || line.find("cannot send its rows") != std::string::npos;
            stop_line = stop_line || line.find("1 table row(s) were lost during this run") != std::string::npos;
        }
        Check(unavailable, "the unavailable sensor was not reported");
        Check(stop_line, "the stop line did not count the row");
    }

    // A host without table sensors pays nothing: no pipeline, so no zstd context, no tick, no thread.
    void HostWithoutTableSensors()
    {
        Collector collector;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_int_sensor(collector.value, "plain", &sensor) == HSM_RESULT_OK, "plain sensor");
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        Check(tables::test::Pipeline(collector.value) == nullptr, "a pipeline exists while running");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start again");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop again");
        Check(tables::test::Pipeline(collector.value) == nullptr, "a pipeline exists after stop");
        hsm_sensor_release(sensor);

        // The first table sensor makes it, with no thread while stopped and no compressor before a pack.
        Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_INT64 } });
        auto* pipeline = tables::test::Pipeline(collector.value);
        Check(pipeline != nullptr, "no pipeline after the first table sensor");
        Check(!pipeline->TickAlive() && !pipeline->DeliveryThreadAlive(), "threads while stopped");
        Check(!pipeline->HasWriter(), "a compressor before any pack");
    }

    // A handle may outlive its collector: every call is refused, nothing is touched after the free.
    void HandleOutlivesCollector()
    {
        auto collector = std::make_unique<Collector>();
        Table table = CreateTable(collector->value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        const std::string path = hsm_table_sensor_path(table.value);
        Check(hsm_collector_start(collector->value) == HSM_RESULT_OK, "start");
        collector.reset();
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(AddDouble(table, 0, 1)), "add after destroy");
        CheckEqual(path, std::string(hsm_table_sensor_path(table.value)), "path after destroy");
        CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, "counted after destroy");
    }

    void WrapperMatchesAbi()
    {
        hsm::collector::CollectorOptions options;
        options.access_key = "k";
        options.server_address = "https://localhost";
        options.module = "m";
        options.computer_name = "c";
        hsm::collector::Collector collector(options);
        hsm::collector::TableSensorOptions table_options;
        table_options.columns = { hsm::collector::TableColumn::Double("bid"), hsm::collector::TableColumn::Int64("size") };
        table_options.location = hsm::collector::SensorLocation::Module;
        hsm::collector::TableSensor quotes = collector.CreateTableSensor("quotes / EURUSD", table_options);
        CheckEqual(std::string("c/m/quotes/EURUSD"), quotes.Path(), "path");

        bool threw = false;
        try
        {
            collector.CreateTableSensor("x", hsm::collector::TableSensorOptions{});
        }
        catch (const hsm::collector::Error& error)
        {
            threw = std::string(error.what()).find("at least one value column") != std::string::npos;
        }
        Check(threw, "create without columns did not throw with the reason");

        threw = false;
        try
        {
            quotes.AddRow(std::chrono::system_clock::now(), { 1.0 });
        }
        catch (const hsm::collector::Error&)
        {
            threw = true;
        }
        Check(threw, "a wrong cell count did not throw");
        threw = false;
        try
        {
            quotes.AddRow(std::chrono::system_clock::now(), { 1.0, 2.5 });
        }
        catch (const hsm::collector::Error&)
        {
            threw = true;
        }
        Check(threw, "a double in an int64 column did not throw");

        // Not running: a drop, false, counted.
        Check(!quotes.AddRow(std::chrono::system_clock::now(), { 1, hsm::collector::TableCell::Null() }), "taken while stopped");
        const auto stats = quotes.Stats();
        CheckEqual(std::uint64_t{ 1 }, stats.rows_dropped, "dropped");
        Check(stats.state == hsm::collector::TableSensorState::Unavailable && !stats.reason.empty(), "unavailable with a reason");
        hsm::collector::TableSensor moved = std::move(quotes);
        Check(quotes.handle() == nullptr && moved.handle() != nullptr, "move");
    }

#if defined(HSM_COLLECTOR_TABLES)
    namespace reader = hsm::collector::tables::test;

    // ---- The recording channel (the scripted server side of the tests) -------------------------------

    class RecordingChannel : public tables::ITableChannel
    {
    public:
        struct Row
        {
            std::uint64_t ticks;
            std::vector<std::uint64_t> values;
            std::vector<bool> nulls;
        };

        struct PutRecord
        {
            tables::TableProducerId producer{};
            std::int64_t sequence = 0;
            std::size_t frame_bytes = 0;
            tables::TableBlockOutcome outcome = tables::TableBlockOutcome::NotDelivered;
            std::set<int> refused;
            reader::ReadBlock block;

            std::vector<std::string> TableNames() const
            {
                std::vector<std::string> names;
                for (const auto& table : block.tables)
                    names.emplace_back(table.id.begin(), table.id.end());
                return names;
            }
        };

        using Answer = std::function<tables::TableBlockAck(const PutRecord&)>;

        std::size_t MaxBlockBytes() const override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return max_block_;
        }

        tables::TableBlockLimits Limits() const override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return limits_;
        }

        tables::TableRegistrationResult Register(const std::vector<tables::TableRegistration>& registrations) override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            registrations_.push_back(registrations);
            tables::TableRegistrationResult result;
            if (fail_registrations_ > 0)
            {
                --fail_registrations_;
                result.unsettled_reason = "scripted: no answer";
                return result;
            }
            for (const auto& registration : registrations)
            {
                const auto refused = refuse_.find(registration.path);
                if (refused != refuse_.end())
                    result.refused[registration.path] = refused->second;
                else
                    result.accepted.insert(registration.path);
            }
            return result;
        }

        tables::TableBlockAck Put(const tables::TableProducerId& producer, std::int64_t sequence, std::vector<std::uint8_t>& frame) override
        {
            if (on_put)
                on_put();

            PutRecord put;
            put.producer = producer;
            put.sequence = sequence;
            put.frame_bytes = frame.size();
            // The block as the server reads it: every check of the reader.
            put.block = reader::ReadTableBlock(frame.data() + tables::kTableFrameHeaderBytes, frame.size() - tables::kTableFrameHeaderBytes);

            std::lock_guard<std::mutex> guard(mutex_);
            tables::TableBlockAck ack;
            if (cancelled_)
            {
                ack.error = "cancelled";
            }
            else if (!answers_.empty())
            {
                ack = answers_.front()(put);
                answers_.pop_front();
            }
            else if (down_)
            {
                ack.error = "scripted: down";
            }
            else
            {
                ack.outcome = tables::TableBlockOutcome::Stored;
            }
            put.outcome = ack.outcome;
            for (const auto& refusal : ack.refused)
                put.refused.insert(refusal.index);
            puts_.push_back(std::move(put));
            return ack;
        }

        void Cancel() override
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                cancelled_ = true;
            }
            cancel_cv_.notify_all();
        }

        void Reset() override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            cancelled_ = false;
        }

        // ---- Scripting ----
        void SetMaxBlock(std::size_t bytes)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            max_block_ = bytes;
        }
        void SetLimits(const tables::TableBlockLimits& limits)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            limits_ = limits;
        }
        void RefuseRegistration(const std::string& path, const std::string& reason)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            refuse_[path] = reason;
        }
        void FailRegistrations(int count)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            fail_registrations_ += count;
        }
        void SetDown(bool down)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            down_ = down;
        }
        void AnswerNext(Answer answer)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            answers_.push_back(std::move(answer));
        }
        // Blocks a Put until Cancel (the stop's drain budget expiring).
        void WaitForCancel()
        {
            std::unique_lock<std::mutex> lock(mutex_);
            cancel_cv_.wait(lock, [this] { return cancelled_; });
        }

        // ---- Records ----
        std::vector<std::vector<tables::TableRegistration>> Registrations() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return registrations_;
        }
        std::vector<PutRecord> Puts() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return puts_;
        }
        std::vector<PutRecord> Stored() const
        {
            std::vector<PutRecord> stored;
            for (auto& put : Puts())
                if (put.outcome == tables::TableBlockOutcome::Stored)
                    stored.push_back(std::move(put));
            return stored;
        }
        // The rows of `path` over every stored block, refused tables excluded.
        std::vector<Row> RowsOf(const std::string& path) const
        {
            std::vector<Row> rows;
            for (const auto& put : Stored())
            {
                const auto names = put.TableNames();
                const auto it = std::find(names.begin(), names.end(), path);
                if (it == names.end())
                    continue;
                const int index = static_cast<int>(it - names.begin());
                if (put.refused.count(index) != 0)
                    continue;
                const auto& table = put.block.tables[static_cast<std::size_t>(index)];
                for (std::int32_t row = 0; row < table.rows; ++row)
                {
                    Row out;
                    out.ticks = table.columns[0].values[static_cast<std::size_t>(row)];
                    for (std::size_t column = 1; column < table.columns.size(); ++column)
                    {
                        out.values.push_back(table.columns[column].values[static_cast<std::size_t>(row)]);
                        out.nulls.push_back(table.columns[column].IsNull(static_cast<std::size_t>(row)));
                    }
                    rows.push_back(std::move(out));
                }
            }
            return rows;
        }

        std::function<void()> on_put;

    private:
        mutable std::mutex mutex_;
        std::condition_variable cancel_cv_;
        std::size_t max_block_ = 4 * 1024 * 1024 - 40;
        tables::TableBlockLimits limits_{};
        std::vector<std::vector<tables::TableRegistration>> registrations_;
        std::vector<PutRecord> puts_;
        std::map<std::string, std::string> refuse_;
        std::deque<Answer> answers_;
        int fail_registrations_ = 0;
        bool down_ = false;
        bool cancelled_ = false;
    };
#endif
} // namespace

// ---- The conformance verbs (driven by hsm_collector_tests.cpp) -------------------------------------------

struct TableConformanceState
{
#if defined(HSM_COLLECTOR_TABLES)
    std::shared_ptr<RecordingChannel> channel;
#endif
    struct Sensor
    {
        std::unique_ptr<Table> handle;
        std::vector<hsm_table_column_type_t> types;
    };
    std::map<std::string, Sensor> sensors;
    std::string results;
};

std::shared_ptr<TableConformanceState> NewTableConformanceState()
{
    return std::make_shared<TableConformanceState>();
}

// Every collector of the corpus gets the recording channel and the manual tick (a collector without table
// sensors has no pipeline, so this costs the other fixtures nothing); create_collector_without_table_transport
// gets neither channel.
void PrepareTableConformanceCollector(TableConformanceState& state, hsm_collector_t* collector, bool with_channel)
{
    state.sensors.clear();
    state.results.clear();
#if defined(HSM_COLLECTOR_TABLES)
    // Without a channel the collector has none, and the record stays empty (the managed driver's
    // recording sender keeps its scripted table channel either way).
    state.channel = std::make_shared<RecordingChannel>();
    if (with_channel)
        tables::test::InstallChannel(collector, state.channel);
#else
    (void)with_channel;
#endif
    tables::test::SetManualTick(collector, true);
}

namespace
{
#if defined(HSM_COLLECTOR_TABLES)
    std::int64_t ToInt64(const std::string& text)
    {
        return std::stoll(text);
    }

    double ParseTableNumber(const std::string& text)
    {
        if (text == "NaN")
            return std::numeric_limits<double>::quiet_NaN();
        if (text == "Infinity")
            return std::numeric_limits<double>::infinity();
        if (text == "-Infinity")
            return -std::numeric_limits<double>::infinity();
        return std::stod(text);
    }

    std::string ExpandRepeat(const std::string& value)
    {
        // repeat:<char>:<count>
        if (value.rfind("repeat:", 0) != 0)
            return value;
        const auto separator = value.find(':', 7);
        return std::string(static_cast<std::size_t>(std::stoul(value.substr(separator + 1))), value[7]);
    }

    std::vector<hsm_table_column_t> ParseColumns(const std::string& text, std::vector<std::string>& names)
    {
        std::vector<hsm_table_column_t> columns;
        names.clear();
        if (text == "none")
            return columns;
        std::size_t start = 0;
        while (start <= text.size())
        {
            auto end = text.find(',', start);
            if (end == std::string::npos)
                end = text.size();
            const std::string pair = text.substr(start, end - start);
            const auto colon = pair.find(':');
            names.push_back(pair.substr(0, colon));
            const std::string type = pair.substr(colon + 1);
            if (type != "double" && type != "int64")
                throw std::runtime_error("Unknown table column type '" + type + "' (double, int64).");
            columns.push_back({ nullptr, type == "double" ? HSM_TABLE_COLUMN_DOUBLE : HSM_TABLE_COLUMN_INT64 });
            start = end + 1;
        }
        for (std::size_t i = 0; i < columns.size(); ++i)
            columns[i].name = names[i].c_str();
        return columns;
    }

    TableConformanceState::Sensor& SensorNamed(TableConformanceState& state, const std::string& name)
    {
        const auto it = state.sensors.find(name);
        if (it == state.sensors.end())
            throw std::runtime_error("No table sensor '" + name + "' was created in this case.");
        return it->second;
    }

    std::string PathOf(TableConformanceState& state, const std::string& name)
    {
        return hsm_table_sensor_path(SensorNamed(state, name).handle->value);
    }

    RecordingChannel& ChannelOf(TableConformanceState& state)
    {
        if (!state.channel)
            throw std::runtime_error("this case has no table channel");
        return *state.channel;
    }

    const char* StateName(hsm_table_sensor_state_t state)
    {
        switch (state)
        {
        case HSM_TABLE_SENSOR_PENDING:
            return "pending";
        case HSM_TABLE_SENSOR_REGISTERED:
            return "registered";
        case HSM_TABLE_SENSOR_REFUSED:
            return "refused";
        case HSM_TABLE_SENSOR_UNAVAILABLE:
            return "unavailable";
        }
        return "?";
    }

    const char* TypeName(tables::TableColumnType type)
    {
        switch (type)
        {
        case tables::TableColumnType::Time:
            return "time";
        case tables::TableColumnType::Int64:
            return "int64";
        case tables::TableColumnType::Double:
            return "double";
        }
        return "?";
    }

    RecordingChannel::Answer ScriptedAnswer(const std::string& outcome)
    {
        using tables::TableBlockAck;
        using tables::TableBlockOutcome;
        if (outcome == "stored")
            return [](const RecordingChannel::PutRecord&) { TableBlockAck ack; ack.outcome = TableBlockOutcome::Stored; return ack; };
        if (outcome == "duplicate")
            return [](const RecordingChannel::PutRecord&) { TableBlockAck ack; ack.outcome = TableBlockOutcome::Duplicate; return ack; };
        if (outcome == "all_refused")
            return [](const RecordingChannel::PutRecord& put) {
                TableBlockAck ack;
                ack.outcome = TableBlockOutcome::AllRefused;
                const auto names = put.TableNames();
                for (std::size_t i = 0; i < names.size(); ++i)
                    ack.refused.push_back({ static_cast<std::int32_t>(i), names[i], "SensorCreatedAfterBlock" });
                ack.refused_count = static_cast<std::int32_t>(names.size());
                return ack;
            };
        if (outcome == "invalid")
            return [](const RecordingChannel::PutRecord&) { TableBlockAck ack; ack.outcome = TableBlockOutcome::Invalid; ack.error = "invalid block: scripted"; return ack; };
        if (outcome == "not_delivered")
            return [](const RecordingChannel::PutRecord&) { TableBlockAck ack; ack.error = "scripted: 503"; ack.retry_after_ms = 0; return ack; };
        throw std::runtime_error("Unknown block answer '" + outcome + "' (stored, duplicate, all_refused, invalid, not_delivered).");
    }

    std::uint64_t TicksOfMs(const std::string& ms)
    {
        return static_cast<std::uint64_t>(tables::kUnixEpochTicks + ToInt64(ms) * 10000);
    }
#endif
} // namespace

// True when `step` is a table verb (and was run); false for any other verb.
bool ExecuteTableConformanceStep(TableConformanceState& state, hsm_collector_t* collector, const std::vector<std::string>& step)
{
    static const std::set<std::string> verbs = {
        "create_table_sensor",
        "create_table_sensor_at_product",
        "expect_create_table_sensor_rejected",
        "add_table_row",
        "add_table_rows",
        "table_tick",
        "table_server_refuse_registration",
        "table_server_fail_registrations",
        "table_server_down",
        "table_server_up",
        "table_server_frame_limit",
        "table_server_answer_next",
        "table_server_refuse_table_next",
        "expect_table_add_results",
        "expect_table_path",
        "expect_table_state",
        "expect_table_rows_accepted",
        "expect_table_rows_dropped",
        "expect_table_registration_count",
        "expect_table_registration_size",
        "expect_table_registration",
        "expect_table_blocks",
        "expect_table_block_attempts",
        "expect_table_block_tables",
        "expect_table_blocks_within",
        "expect_table_sequences",
        "expect_table_sequences_increasing",
        "expect_table_rows",
        "expect_table_row",
    };
    const std::string& action = step[0];
    if (verbs.count(action) == 0)
        return false;

#if !defined(HSM_COLLECTOR_TABLES)
    (void)state;
    (void)collector;
    throw std::runtime_error("The table verbs need the tables build (HSM_COLLECTOR_TABLES): " + action);
#else
    const auto arg = [&](std::size_t index) -> const std::string& {
        if (index + 1 >= step.size())
            throw std::runtime_error(action + ": argument " + std::to_string(index) + " is missing");
        return step[index + 1];
    };

    if (action == "create_table_sensor" || action == "create_table_sensor_at_product")
    {
        std::vector<std::string> names;
        auto columns = ParseColumns(arg(1), names);
        hsm_table_sensor_options_t options = hsm_table_sensor_options_default();
        options.sensor_location = action == "create_table_sensor_at_product" ? 1 : 0;
        hsm_table_sensor_t* sensor = nullptr;
        const auto result = hsm_collector_create_table_sensor(collector, ExpandRepeat(arg(0)).c_str(), columns.data(), columns.size(), &options, &sensor);
        Check(result == HSM_RESULT_OK, action + " " + arg(0) + " failed: " + std::string(hsm_collector_last_error(collector) ? hsm_collector_last_error(collector) : ""));
        TableConformanceState::Sensor entry;
        entry.handle = std::make_unique<Table>(sensor);
        for (const auto& column : columns)
            entry.types.push_back(column.type);
        state.sensors[arg(0)] = std::move(entry);
        return true;
    }
    if (action == "expect_create_table_sensor_rejected")
    {
        std::vector<std::string> names;
        auto columns = ParseColumns(arg(1), names);
        hsm_table_sensor_t* sensor = nullptr;
        const auto result = hsm_collector_create_table_sensor(collector, ExpandRepeat(arg(0)).c_str(), columns.data(), columns.size(), nullptr, &sensor);
        hsm_table_sensor_release(sensor);
        Check(result == HSM_RESULT_INVALID_ARGUMENT, "create_table_sensor " + arg(0) + " was not rejected (" + std::to_string(static_cast<int>(result)) + ")");
        return true;
    }
    if (action == "add_table_row")
    {
        auto& sensor = SensorNamed(state, arg(0));
        const std::size_t columns = sensor.types.size();
        std::vector<hsm_table_cell_t> cells(columns);
        std::vector<std::uint8_t> nulls((columns + 7) / 8, 0);
        bool any_null = false;
        for (std::size_t c = 0; c < columns; ++c)
        {
            const std::string& text = arg(2 + c);
            if (text == "token:null")
            {
                cells[c].i64 = 0;
                nulls[c / 8] = static_cast<std::uint8_t>(nulls[c / 8] | (1u << (c % 8)));
                any_null = true;
            }
            else if (sensor.types[c] == HSM_TABLE_COLUMN_INT64)
                cells[c].i64 = ToInt64(text);
            else
                cells[c].f64 = ParseTableNumber(text);
        }
        const auto result = hsm_table_sensor_add_row(sensor.handle->value, ToInt64(arg(1)) * 1000000, cells.data(), columns, any_null ? nulls.data() : nullptr);
        Check(result != HSM_RESULT_INVALID_ARGUMENT, "add_table_row: wrong shape");
        state.results += result == HSM_RESULT_OK ? '1' : '0';
        return true;
    }
    if (action == "add_table_rows")
    {
        // path|count|start_ms: row r has the time start_ms + r and the value r in every column.
        auto& sensor = SensorNamed(state, arg(0));
        const std::size_t columns = sensor.types.size();
        const auto count = static_cast<std::size_t>(ToInt64(arg(1)));
        const std::int64_t start = ToInt64(arg(2));
        std::vector<std::int64_t> times(count);
        std::vector<hsm_table_cell_t> cells(count * columns);
        for (std::size_t row = 0; row < count; ++row)
        {
            times[row] = (start + static_cast<std::int64_t>(row)) * 1000000;
            for (std::size_t c = 0; c < columns; ++c)
            {
                if (sensor.types[c] == HSM_TABLE_COLUMN_INT64)
                    cells[row * columns + c].i64 = static_cast<std::int64_t>(row);
                else
                    cells[row * columns + c].f64 = static_cast<double>(row);
            }
        }
        std::size_t taken = 0;
        const auto result = hsm_table_sensor_add_rows(sensor.handle->value, times.data(), cells.data(), nullptr, count, &taken);
        Check(result != HSM_RESULT_INVALID_ARGUMENT, "add_table_rows: wrong shape");
        state.results += taken == count ? '1' : '0';
        return true;
    }
    if (action == "table_tick")
    {
        tables::test::RunTick(collector);
        return true;
    }
    if (action == "table_server_refuse_registration")
    {
        ChannelOf(state).RefuseRegistration(PathOf(state, arg(0)), arg(1));
        return true;
    }
    if (action == "table_server_fail_registrations")
    {
        ChannelOf(state).FailRegistrations(static_cast<int>(ToInt64(arg(0))));
        return true;
    }
    if (action == "table_server_down" || action == "table_server_up")
    {
        ChannelOf(state).SetDown(action == "table_server_down");
        return true;
    }
    if (action == "table_server_frame_limit")
    {
        ChannelOf(state).SetMaxBlock(static_cast<std::size_t>(ToInt64(arg(0))) - tables::kTableFrameHeaderBytes);
        return true;
    }
    if (action == "table_server_answer_next")
    {
        ChannelOf(state).AnswerNext(ScriptedAnswer(arg(0)));
        return true;
    }
    if (action == "table_server_refuse_table_next")
    {
        const std::string path = PathOf(state, arg(0));
        const std::string reason = arg(1);
        ChannelOf(state).AnswerNext([path, reason](const RecordingChannel::PutRecord& put) {
            tables::TableBlockAck ack;
            ack.outcome = tables::TableBlockOutcome::Stored;
            const auto names = put.TableNames();
            const auto it = std::find(names.begin(), names.end(), path);
            ack.refused.push_back({ it == names.end() ? -1 : static_cast<std::int32_t>(it - names.begin()), path, reason });
            ack.refused_count = 1;
            return ack;
        });
        return true;
    }
    if (action == "expect_table_add_results")
    {
        Check(arg(0) == state.results, "table add results: expected " + arg(0) + ", actual " + state.results);
        return true;
    }
    if (action == "expect_table_path")
    {
        CheckEqual(arg(1), PathOf(state, arg(0)), "table path");
        return true;
    }
    if (action == "expect_table_state" || action == "expect_table_rows_accepted" || action == "expect_table_rows_dropped")
    {
        const auto stats = Stats(*SensorNamed(state, arg(0)).handle);
        if (action == "expect_table_state")
            CheckEqual(arg(1), std::string(StateName(stats.state)), "table state of " + arg(0));
        else if (action == "expect_table_rows_accepted")
            CheckEqual(static_cast<std::uint64_t>(ToInt64(arg(1))), stats.rows_accepted, "rows accepted by " + arg(0));
        else
            CheckEqual(static_cast<std::uint64_t>(ToInt64(arg(1))), stats.rows_dropped, "rows dropped by " + arg(0));
        return true;
    }
    if (action == "expect_table_registration_count")
    {
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(0))), ChannelOf(state).Registrations().size(), "table registration requests");
        return true;
    }
    if (action == "expect_table_registration_size")
    {
        const auto requests = ChannelOf(state).Registrations();
        const auto index = static_cast<std::size_t>(ToInt64(arg(0)));
        Check(index < requests.size(), "no registration request " + arg(0));
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(1))), requests[index].size(), "registrations in request " + arg(0));
        return true;
    }
    if (action == "expect_table_registration")
    {
        const auto requests = ChannelOf(state).Registrations();
        const auto index = static_cast<std::size_t>(ToInt64(arg(0)));
        Check(index < requests.size(), "no registration request " + arg(0));
        const std::string path = PathOf(state, arg(1));
        int found = 0;
        for (const auto& registration : requests[index])
        {
            if (registration.path != path)
                continue;
            ++found;
            std::string columns;
            for (const auto& column : registration.columns)
                columns += (columns.empty() ? "" : ",") + column.name + ":" + TypeName(column.type);
            CheckEqual(arg(2), columns, "registered columns of " + path);
        }
        CheckEqual(1, found, "registrations of " + path + " in request " + arg(0));
        return true;
    }
    if (action == "expect_table_blocks")
    {
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(0))), ChannelOf(state).Stored().size(), "stored blocks");
        return true;
    }
    if (action == "expect_table_block_attempts")
    {
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(0))), ChannelOf(state).Puts().size(), "block attempts");
        return true;
    }
    if (action == "expect_table_block_tables")
    {
        const auto stored = ChannelOf(state).Stored();
        const auto index = static_cast<std::size_t>(ToInt64(arg(0)));
        Check(index < stored.size(), "no stored block " + arg(0));
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(1))), stored[index].block.tables.size(), "tables of block " + arg(0));
        return true;
    }
    if (action == "expect_table_blocks_within")
    {
        for (const auto& put : ChannelOf(state).Puts())
            Check(put.frame_bytes <= static_cast<std::size_t>(ToInt64(arg(0))), "a frame of " + std::to_string(put.frame_bytes) + " bytes");
        return true;
    }
    if (action == "expect_table_sequences" || action == "expect_table_sequences_increasing")
    {
        const auto puts = ChannelOf(state).Puts();
        std::string sequences;
        std::set<tables::TableProducerId> producers;
        for (std::size_t i = 0; i < puts.size(); ++i)
        {
            sequences += (i == 0 ? "" : ",") + std::to_string(puts[i].sequence);
            producers.insert(puts[i].producer);
            if (action == "expect_table_sequences_increasing" && i > 0)
                Check(puts[i].sequence >= puts[i - 1].sequence, "sequence " + std::to_string(puts[i].sequence) + " after " + std::to_string(puts[i - 1].sequence));
        }
        if (action == "expect_table_sequences")
            CheckEqual(arg(0), sequences, "producer sequences");
        CheckEqual(std::size_t{ 1 }, producers.size(), "producers");
        return true;
    }
    if (action == "expect_table_rows")
    {
        CheckEqual(static_cast<std::size_t>(ToInt64(arg(1))), ChannelOf(state).RowsOf(PathOf(state, arg(0))).size(), "stored rows of " + arg(0));
        return true;
    }
    if (action == "expect_table_row")
    {
        auto& sensor = SensorNamed(state, arg(0));
        const auto rows = ChannelOf(state).RowsOf(PathOf(state, arg(0)));
        const auto index = static_cast<std::size_t>(ToInt64(arg(1)));
        Check(index < rows.size(), "no stored row " + arg(1) + " of " + arg(0));
        const auto& row = rows[index];
        CheckEqual(TicksOfMs(arg(2)), row.ticks, "time of row " + arg(1));
        for (std::size_t c = 0; c < sensor.types.size(); ++c)
        {
            const std::string& expected = arg(3 + c);
            const std::string what = "column " + std::to_string(c) + " of row " + arg(1) + " of " + arg(0);
            if (expected == "token:null")
            {
                Check(row.nulls[c], what + " has a value");
                continue;
            }
            Check(!row.nulls[c], what + " is null");
            const double wanted = ParseTableNumber(expected);
            double actual = 0;
            if (sensor.types[c] == HSM_TABLE_COLUMN_INT64)
                actual = static_cast<double>(static_cast<std::int64_t>(row.values[c]));
            else
                std::memcpy(&actual, &row.values[c], sizeof actual);
            Check(std::isnan(wanted) ? std::isnan(actual) : (actual == wanted && std::signbit(actual) == std::signbit(wanted)),
                  what + ": expected " + expected + ", actual " + std::to_string(actual));
        }
        return true;
    }
    return false;
#endif
}

#if defined(HSM_COLLECTOR_TABLES)
namespace
{
    // ---- The pipeline with threads -------------------------------------------------------------------

    struct ThreadedCollector
    {
        Collector collector;
        std::shared_ptr<RecordingChannel> channel = std::make_shared<RecordingChannel>();

        explicit ThreadedCollector(std::int64_t request_timeout_ms = 0)
            : collector(OptionsWithTimeout(request_timeout_ms))
        {
            hsm_collector_test_install_manual_clock(collector.value, 1000000);
            tables::test::InstallChannel(collector.value, channel);
            hsm_table_options_t options{};
            options.struct_size = sizeof options;
            options.tick_period_ms = 1000;
            Check(hsm_collector_set_table_options(collector.value, &options) == HSM_RESULT_OK, "table options");
        }

        static hsm_collector_options_t OptionsWithTimeout(std::int64_t timeout)
        {
            auto options = Options();
            options.request_timeout_ms = static_cast<int32_t>(timeout);
            return options;
        }

        tables::TablePipeline& Pipeline() { return *tables::test::Pipeline(collector.value); }
    };

    // The threads of a run exist from Start to Stop, and none after: a tick, a registration answer or an
    // ack of one run can never reach the next (the managed collector needed a run object for that).
    void ThreadsOnlyWhileRunning()
    {
        ThreadedCollector host;
        Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        auto& pipeline = host.Pipeline();
        for (int run = 1; run <= 3; ++run)
        {
            Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
            Check(pipeline.TickAlive() && pipeline.DeliveryThreadAlive(), "threads after start");
            CheckEqual(std::int64_t{ run }, pipeline.RunCount(), "runs");
            WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, "the registration");
            Check(AddDouble(table, run, run) == HSM_RESULT_OK, "add");
            Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop");
            Check(!pipeline.TickAlive() && !pipeline.DeliveryThreadAlive(), "threads after stop");
            CheckEqual(std::int64_t{ 0 }, pipeline.ReservedBytes(), "reserved bytes after stop");
            CheckEqual(std::size_t{ 0 }, pipeline.BacklogBlocks(), "backlog after stop");
        }
        // Each run delivered its own row under its own producer, at sequence 0.
        const auto puts = host.channel->Puts();
        CheckEqual(std::size_t{ 3 }, puts.size(), "puts");
        std::set<tables::TableProducerId> producers;
        for (const auto& put : puts)
        {
            producers.insert(put.producer);
            CheckEqual(std::int64_t{ 0 }, put.sequence, "sequence of a run's first block");
        }
        CheckEqual(std::size_t{ 3 }, producers.size(), "a producer per run");
        CheckEqual(std::size_t{ 3 }, host.channel->RowsOf(hsm_table_sensor_path(table.value)).size(), "rows");
        CheckEqual(std::uint64_t{ 0 }, Stats(table).rows_dropped, "dropped");
    }

    // Unavailable is decided at each Start: a channel installed later makes the sensor Pending, and a
    // Refused one stays refused.
    void UnavailableDecidedAtEachStart()
    {
        Collector collector;
        tables::test::SetManualTick(collector.value, true);
        Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(table).state), "without a channel");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        auto channel = std::make_shared<RecordingChannel>();
        tables::test::InstallChannel(collector.value, channel);
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start again");
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_PENDING), static_cast<int>(Stats(table).state), "with a channel");
        CheckEqual(std::string(), Reason(table), "reason cleared");
        Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
        tables::test::RunTick(collector.value);
        CheckEqual(std::size_t{ 1 }, channel->RowsOf(hsm_table_sensor_path(table.value)).size(), "delivered");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop again");
    }

    // A writer preempted between its reservation and its append across a whole Stop and Start: its row
    // belongs to the run that was accepting when it appended, the buffer bound stays exact, and the row
    // goes out under that run's producer only.
    void WriterAcrossStopAndStart()
    {
        ThreadedCollector host;
        Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        auto& pipeline = host.Pipeline();
        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
        WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, "the registration");
        const auto first_producer = pipeline.Producer();

        std::mutex mutex;
        std::condition_variable cv;
        bool parked = false;
        bool release = false;
        pipeline.Shared().before_append = [&] {
            std::unique_lock<std::mutex> lock(mutex);
            parked = true;
            cv.notify_all();
            cv.wait(lock, [&] { return release; });
        };

        hsm_result_t result = HSM_RESULT_INTERNAL_ERROR;
        std::thread writer([&] { result = AddDouble(table, 42, 4.2); });
        {
            std::unique_lock<std::mutex> lock(mutex);
            if (!cv.wait_for(lock, std::chrono::seconds(60), [&] { return parked; }))
                throw std::runtime_error("the writer never reserved");
        }
        CheckEqual(std::int64_t{ 16 }, pipeline.ReservedBytes(), "the parked reservation");
        Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop");
        CheckEqual(std::int64_t{ 16 }, pipeline.ReservedBytes(), "the stop kept the writer's reservation");
        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start again");
        const auto second_producer = pipeline.Producer();
        Check(first_producer != second_producer, "a new producer");
        {
            std::lock_guard<std::mutex> lock(mutex);
            release = true;
        }
        cv.notify_all();
        writer.join();
        pipeline.Shared().before_append = nullptr;
        CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(result), "the second run took the row");
        Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop again");
        CheckEqual(std::int64_t{ 0 }, pipeline.ReservedBytes(), "every reservation went back");

        // Registered in the second run before its stop? Then delivered under the second producer only;
        // else dropped and counted at that stop. Never under the first.
        const auto rows = host.channel->RowsOf(hsm_table_sensor_path(table.value));
        for (const auto& put : host.channel->Stored())
            Check(put.producer == second_producer, "a block under the first run's producer");
        CheckEqual(std::uint64_t{ 1 }, rows.size() + Stats(table).rows_dropped, "the row is delivered or counted");
    }

    // The stop's drain is bounded: a put that never answers is cancelled when the budget runs out, and
    // its rows are counted as not delivered at stop.
    void StopDrainIsBounded()
    {
        ThreadedCollector host(/*request_timeout_ms=*/1000);
        Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
        WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, "the registration");
        Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
        std::atomic<bool> hung{ false };
        host.channel->on_put = [&] {
            hung = true;
            host.channel->WaitForCancel();
        };
        Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop");
        Check(hung.load(), "the drain never put the block");
        CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, "the undelivered row");
        CheckEqual(std::int64_t{ 1 }, host.Pipeline().Ledger().Total(tables::TableDropReason::NotDeliveredAtStop), "its reason");
        Check(!host.Pipeline().DeliveryThreadAlive(), "a delivery thread after stop");
    }

    // Rows refused while the collector is stopping are counted in the run's losses and named by the
    // one stop line; refused after the stop line, on the sensor only.
    void LossesAtStopReachTheStopLine()
    {
        Collector collector;
        auto channel = std::make_shared<RecordingChannel>();
        tables::test::InstallChannel(collector.value, channel);
        tables::test::SetManualTick(collector.value, true);
        std::vector<std::string> lines;
        hsm_collector_set_logger(
            collector.value, [](hsm_log_level_t, const char* message, void* user) { static_cast<std::vector<std::string>*>(user)->push_back(message); }, &lines);
        Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        tables::test::RunTick(collector.value);
        Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
        channel->SetDown(true);
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, "dropped at stop");
        bool stop_line = false;
        for (const auto& line : lines)
            stop_line = stop_line || line.find("1 table row(s) were lost during this run") != std::string::npos;
        Check(stop_line, "no stop line for the undelivered row");
        Check(AddDouble(table, 2, 2) == HSM_RESULT_INVALID_STATE, "add after stop");
        CheckEqual(std::uint64_t{ 2 }, Stats(table).rows_dropped, "counted on the sensor");
        CheckEqual(std::int64_t{ 0 }, tables::test::Pipeline(collector.value)->Ledger().Total(tables::TableDropReason::NotRunning), "a closed ledger took it");
    }

    // A block over the frame limit is split by tables, then rows; a row that cannot fit is dropped.
    // Too large is final, and a sequence that was on the wire is never given to other rows (#1848, the
    // defects the managed review found): N blocks the channel answers "too large" cost exactly N puts and
    // N dropped rows, the next block takes the next sequence, and a row larger than the frame limit on
    // its own is dropped at packing, once, with no put at all.
    void TooLargeIsFinalAndSequencesNeverGoBack()
    {
        Collector collector;
        auto channel = std::make_shared<RecordingChannel>();
        tables::test::InstallChannel(collector.value, channel);
        tables::test::SetManualTick(collector.value, true);
        Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        tables::test::RunTick(collector.value);

        constexpr int kRefused = 5;
        for (int i = 0; i < kRefused; ++i)
        {
            channel->AnswerNext([](const RecordingChannel::PutRecord&) {
                tables::TableBlockAck ack;
                ack.outcome = tables::TableBlockOutcome::TooLarge;
                ack.error = "scripted: frame too large";
                return ack;
            });
            Check(AddDouble(table, i, i) == HSM_RESULT_OK, "add");
            tables::test::RunTick(collector.value);
        }
        // More ticks with nothing new: nothing is sent again.
        tables::test::RunTick(collector.value);
        tables::test::RunTick(collector.value);
        Check(AddDouble(table, 99, 99) == HSM_RESULT_OK, "add after the refusals");
        tables::test::RunTick(collector.value);

        const auto puts = channel->Puts();
        CheckEqual(std::size_t{ kRefused + 1 }, puts.size(), "puts");
        for (std::size_t i = 0; i < puts.size(); ++i)
            CheckEqual(static_cast<std::int64_t>(i), puts[i].sequence, "sequence of put " + std::to_string(i));
        CheckEqual(std::uint64_t{ kRefused }, Stats(table).rows_dropped, "dropped");
        auto* pipeline = tables::test::Pipeline(collector.value);
        CheckEqual(std::int64_t{ kRefused }, pipeline->Ledger().Total(tables::TableDropReason::RowTooLarge), "counted once each");
        CheckEqual(std::size_t{ 1 }, channel->RowsOf(hsm_table_sensor_path(table.value)).size(), "the later row");

        // A row that alone breaks the frame limit: dropped at packing, never put.
        channel->SetMaxBlock(30);
        for (int i = 0; i < 3; ++i)
            Check(AddDouble(table, 1000 + i, i) == HSM_RESULT_OK, "add");
        tables::test::RunTick(collector.value);
        tables::test::RunTick(collector.value);
        CheckEqual(puts.size(), channel->Puts().size(), "puts of rows that cannot fit");
        CheckEqual(std::int64_t{ kRefused + 3 }, pipeline->Ledger().Total(tables::TableDropReason::RowTooLarge), "the split ends at one row");
        CheckEqual(std::uint64_t{ kRefused + 3 }, Stats(table).rows_dropped, "every row that cannot fit is counted");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        CheckEqual(puts.size(), channel->Puts().size(), "the stop sent nothing again");
    }

    void SplitKeepsEveryRow()
    {
        Collector collector;
        auto channel = std::make_shared<RecordingChannel>();
        tables::test::InstallChannel(collector.value, channel);
        tables::test::SetManualTick(collector.value, true);
        std::vector<Table> sensors;
        for (const char* name : { "a", "b", "c", "d", "e" })
            sensors.push_back(CreateTable(collector.value, name, { { "x", HSM_TABLE_COLUMN_DOUBLE }, { "y", HSM_TABLE_COLUMN_INT64 } }));
        channel->SetMaxBlock(400);
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        tables::test::RunTick(collector.value);
        std::uint64_t seed = 1;
        for (auto& sensor : sensors)
        {
            std::vector<std::int64_t> times(500);
            std::vector<hsm_table_cell_t> cells(1000);
            for (std::size_t r = 0; r < 500; ++r)
            {
                seed = seed * 6364136223846793005ULL + 1442695040888963407ULL;
                times[r] = static_cast<std::int64_t>(seed >> 20);
                cells[2 * r].i64 = static_cast<std::int64_t>(seed);
                cells[2 * r + 1].i64 = static_cast<std::int64_t>(seed * 31);
            }
            Check(hsm_table_sensor_add_rows(sensor.value, times.data(), cells.data(), nullptr, 500, nullptr) == HSM_RESULT_OK, "add rows");
        }
        tables::test::RunTick(collector.value);
        const auto puts = channel->Puts();
        Check(puts.size() > 5, "no split happened");
        for (const auto& put : puts)
            Check(put.frame_bytes <= 440, "a frame of " + std::to_string(put.frame_bytes));
        for (std::size_t i = 1; i < puts.size(); ++i)
            CheckEqual(puts[i - 1].sequence + 1, puts[i].sequence, "sequences");
        for (auto& sensor : sensors)
            CheckEqual(std::size_t{ 500 }, channel->RowsOf(hsm_table_sensor_path(sensor.value)).size(), "rows of a sensor");

        // A frame too small for one row: that row is dropped and counted.
        channel->SetMaxBlock(30);
        hsm_table_cell_t cells[2];
        cells[0].f64 = 1;
        cells[1].i64 = 2;
        Check(hsm_table_sensor_add_row(sensors[0].value, 1, cells, 2, nullptr) == HSM_RESULT_OK, "add");
        tables::test::RunTick(collector.value);
        CheckEqual(std::int64_t{ 1 }, tables::test::Pipeline(collector.value)->Ledger().Total(tables::TableDropReason::RowTooLarge), "row too large");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
    }

    // A table the channel's limits can never take (its identifier, its column count; the create checks
    // the default limits only) is Unavailable once those limits are known: at Start, or at its creation
    // in a run. Limits that change inside a run do not split such a table down to single rows: its rows
    // of each pack are one "packing failed" loss, reported once.
    void TableOverConnectionLimits()
    {
        Collector collector;
        auto channel = std::make_shared<RecordingChannel>();
        tables::TableBlockLimits limits;
        limits.max_columns_per_table = 3;
        limits.max_table_id_length = 40; // "conformance-host/conformance-module/" is 36 bytes
        channel->SetLimits(limits);
        tables::test::InstallChannel(collector.value, channel);
        tables::test::SetManualTick(collector.value, true);
        std::vector<std::string> lines;
        hsm_collector_set_logger(
            collector.value, [](hsm_log_level_t, const char* message, void* user) { static_cast<std::vector<std::string>*>(user)->push_back(message); }, &lines);
        const auto count_lines = [&](const std::string& text) {
            return std::count_if(lines.begin(), lines.end(), [&](const std::string& line) { return line.find(text) != std::string::npos; });
        };

        Table wide = CreateTable(collector.value, "w", { { "a", HSM_TABLE_COLUMN_DOUBLE }, { "b", HSM_TABLE_COLUMN_DOUBLE }, { "c", HSM_TABLE_COLUMN_DOUBLE } });
        Table named = CreateTable(collector.value, "a-longer-name", { { "a", HSM_TABLE_COLUMN_DOUBLE } });
        Table narrow = CreateTable(collector.value, "n", { { "a", HSM_TABLE_COLUMN_DOUBLE }, { "b", HSM_TABLE_COLUMN_DOUBLE } });
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(wide).state), "too many columns, at create");
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(wide).state), "too many columns");
        Check(Reason(wide).find("4 columns with the time and a table may have 3") != std::string::npos, "reason '" + Reason(wide) + "'");
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(named).state), "too long a name");
        Check(Reason(named).find("a table name may have 40") != std::string::npos, "reason '" + Reason(named) + "'");
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_PENDING), static_cast<int>(Stats(narrow).state), "a table that fits");
        Table late = CreateTable(collector.value, "late", { { "a", HSM_TABLE_COLUMN_DOUBLE }, { "b", HSM_TABLE_COLUMN_DOUBLE }, { "c", HSM_TABLE_COLUMN_DOUBLE } });
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(late).state), "too many columns, created in a run");
        tables::test::RunTick(collector.value);
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_REGISTERED), static_cast<int>(Stats(narrow).state), "registered");
        for (const auto& registrations : channel->Registrations())
            for (const auto& registration : registrations)
                Check(registration.path.find("/n") != std::string::npos, "a table the connection cannot take was registered: " + registration.path);

        // The limits shrink inside the run: 1000 rows a tick, one loss each, one report.
        limits.max_columns_per_table = 2;
        channel->SetLimits(limits);
        auto* pipeline = tables::test::Pipeline(collector.value);
        std::vector<std::int64_t> times(1000);
        std::vector<hsm_table_cell_t> cells(2000);
        for (int tick = 1; tick <= 2; ++tick)
        {
            Check(hsm_table_sensor_add_rows(narrow.value, times.data(), cells.data(), nullptr, 1000, nullptr) == HSM_RESULT_OK, "add rows");
            tables::test::RunTick(collector.value);
            CheckEqual(std::int64_t{ 1000 } * tick, pipeline->Ledger().Total(tables::TableDropReason::PackingFailed), "packing failed after tick " + std::to_string(tick));
            CheckEqual(std::int64_t{ 0 }, pipeline->Ledger().Total(tables::TableDropReason::RowTooLarge), "no row is too large");
            CheckEqual(std::uint64_t{ 1000 } * static_cast<std::uint64_t>(tick), Stats(narrow).rows_dropped, "dropped on the sensor");
            CheckEqual(std::int64_t{ 0 }, pipeline->ReservedBytes(), "reserved bytes");
        }
        CheckEqual(std::size_t{ 0 }, channel->Puts().size(), "puts");
        CheckEqual(static_cast<std::ptrdiff_t>(1), count_lines("cannot be packed"), "reported once");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
    }

    // A row count that cannot be a real array (its rows would take more than INT64_MAX bytes) is a wrong
    // shape on a sensor that accepts rows: refused, counted nowhere (not as a full buffer).
    void ImpossibleRowCountIsAWrongShape()
    {
        Collector collector;
        auto channel = std::make_shared<RecordingChannel>();
        tables::test::InstallChannel(collector.value, channel);
        tables::test::SetManualTick(collector.value, true);
        Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
        tables::test::RunTick(collector.value);
        CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_REGISTERED), static_cast<int>(Stats(table).state), "registered");
        std::int64_t time = 0;
        hsm_table_cell_t cell;
        cell.f64 = 1;
        // 16 bytes a row: INT64_MAX / 16 + 1 rows pass INT64_MAX bytes; SIZE_MAX is an n - 1 below zero.
        const std::size_t counts[] = { SIZE_MAX, static_cast<std::size_t>((std::numeric_limits<std::int64_t>::max)() / 16) + 1,
                                       static_cast<std::size_t>((std::numeric_limits<std::int64_t>::max)()) };
        for (const std::size_t count : counts)
        {
            std::size_t taken = 99;
            CheckEqual(static_cast<int>(HSM_RESULT_INVALID_ARGUMENT), static_cast<int>(hsm_table_sensor_add_rows(table.value, &time, &cell, nullptr, count, &taken)),
                       "rows " + std::to_string(count));
            CheckEqual(std::size_t{ 0 }, taken, "taken");
        }
        CheckEqual(std::uint64_t{ 0 }, Stats(table).rows_dropped, "dropped");
        CheckEqual(std::uint64_t{ 0 }, Stats(table).rows_accepted, "accepted");
        CheckEqual(std::int64_t{ 0 }, tables::test::Pipeline(collector.value)->Ledger().Total(tables::TableDropReason::BufferFull), "buffer full");
        CheckEqual(std::int64_t{ 0 }, tables::test::Pipeline(collector.value)->ReservedBytes(), "reserved");
        Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
    }

    // A table run whose threads cannot be made (the host's thread limit: std::thread throws) is rolled
    // back: the collector's Start returns OK and runs, no thread of the table run is left, rows are
    // refused and counted, the sensors say why; the stop and the next Start work as usual. For the tick's
    // thread and for the delivery thread.
    void StartFailureRollsBack()
    {
        for (int fail_at = 0; fail_at < 2; ++fail_at)
        {
            const std::string what = fail_at == 0 ? "tick thread" : "delivery thread";
            ThreadedCollector host;
            Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
            auto& pipeline = host.Pipeline();
            int calls = 0;
            pipeline.before_thread_start_for_tests = [&] {
                if (calls++ == fail_at)
                    throw std::system_error(std::make_error_code(std::errc::resource_unavailable_try_again));
            };
            CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_start(host.collector.value)), what + ": start");
            CheckEqual(static_cast<int>(HSM_COLLECTOR_STATUS_RUNNING), static_cast<int>(hsm_collector_status(host.collector.value)), what + ": status");
            Check(calls > fail_at, what + ": the seam was not reached");
            Check(!pipeline.Running() && !pipeline.TickAlive() && !pipeline.DeliveryThreadAlive(), what + ": a table thread was left");
            CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_UNAVAILABLE), static_cast<int>(Stats(table).state), what + ": state");
            Check(Reason(table).find("could not start their threads") != std::string::npos, what + ": reason '" + Reason(table) + "'");
            CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(AddDouble(table, 1, 1)), what + ": add");
            CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, what + ": the refused row is counted");
            CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_stop(host.collector.value)), what + ": stop");

            pipeline.before_thread_start_for_tests = nullptr;
            CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_start(host.collector.value)), what + ": start again");
            Check(pipeline.Running() && pipeline.TickAlive() && pipeline.DeliveryThreadAlive(), what + ": threads of the next run");
            WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, what + ": the registration");
            Check(AddDouble(table, 2, 2) == HSM_RESULT_OK, what + ": add in the next run");
            CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_stop(host.collector.value)), what + ": stop again");
            CheckEqual(std::size_t{ 1 }, host.channel->RowsOf(hsm_table_sensor_path(table.value)).size(), what + ": delivered in the next run");
        }
    }

    // A stop whose drain fails (a step that throws) still completes: Stop returns, the delivery thread is
    // joined, what was not delivered is counted; a destroy that runs such a stop deletes the collector.
    void StopFailureCompletes()
    {
        ThreadedCollector host;
        Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
        auto& pipeline = host.Pipeline();
        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
        WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, "the registration");
        host.channel->SetDown(true);
        Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
        pipeline.in_finish_drain_for_tests = [] { throw std::bad_alloc(); };
        CheckEqual(static_cast<int>(HSM_RESULT_OK), static_cast<int>(hsm_collector_stop(host.collector.value)), "stop");
        CheckEqual(static_cast<int>(HSM_COLLECTOR_STATUS_STOPPED), static_cast<int>(hsm_collector_status(host.collector.value)), "status");
        Check(!pipeline.Running() && !pipeline.TickAlive() && !pipeline.DeliveryThreadAlive(), "a table thread after stop");
        CheckEqual(std::int64_t{ 0 }, pipeline.ReservedBytes(), "reserved bytes");
        CheckEqual(std::size_t{ 0 }, pipeline.BacklogBlocks(), "backlog");
        CheckEqual(std::uint64_t{ 1 }, Stats(table).rows_dropped, "the undelivered row is counted");
        CheckEqual(std::int64_t{ 1 }, pipeline.Ledger().Total(tables::TableDropReason::NotDeliveredAtStop), "its reason");

        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start again");
        Check(AddDouble(table, 2, 2) == HSM_RESULT_OK, "add again");
        host.collector.Destroy();
        CheckEqual(static_cast<int>(HSM_RESULT_INVALID_STATE), static_cast<int>(AddDouble(table, 3, 3)), "add after destroy");
        CheckEqual(std::uint64_t{ 3 }, Stats(table).rows_dropped, "every row of the destroyed run is counted");
    }

    // A host log sink that calls back into the collector from inside its callback: it creates a table
    // sensor and reads its stats. A report written under a lock of the collector or of the pipeline that
    // these calls take again would lock a std::mutex twice on one thread (a deadlock, or an error).
    struct ReentrantSink
    {
        hsm_collector_t* collector = nullptr;
        std::atomic<int> table_reports{ 0 };
        std::atomic<int> failures{ 0 };
        inline static thread_local int depth = 0;

        static void Log(hsm_log_level_t, const char* message, void* user)
        {
            auto& sink = *static_cast<ReentrantSink*>(user);
            if (depth > 0)
                return;
            ++depth;
            // Only table reports call back: a call back from a lifecycle line would make the pipeline
            // before the path under test does.
            const std::string text = message;
            if (text.find("table") == std::string::npos && text.find("Table") == std::string::npos)
            {
                --depth;
                return;
            }
            ++sink.table_reports;
            const hsm_table_column_t column{ "v", HSM_TABLE_COLUMN_DOUBLE };
            hsm_table_sensor_t* handle = nullptr;
            const auto created = hsm_collector_create_table_sensor(sink.collector, "reentrant", &column, 1, nullptr, &handle);
            if (created == HSM_RESULT_OK)
            {
                hsm_table_sensor_stats_t stats{};
                stats.struct_size = sizeof stats;
                if (hsm_table_sensor_get_stats(handle, &stats) != HSM_RESULT_OK)
                    ++sink.failures;
                hsm_table_sensor_release(handle);
            }
            else if (created != HSM_RESULT_INVALID_STATE)
            {
                ++sink.failures;
            }
            --depth;
        }

        void Attach(hsm_collector_t* value)
        {
            collector = value;
            hsm_collector_set_logger(value, &ReentrantSink::Log, this);
        }

        // Runs `step` and checks that it wrote a table report and that every call back succeeded.
        template <typename Step>
        void Expect(const std::string& path, Step step)
        {
            const int before = table_reports.load();
            step();
            Check(table_reports.load() > before, path + ": no table report reached the sink");
            CheckEqual(0, failures.load(), path + ": a call back into the collector failed");
        }
    };

    // Every path that writes a table report writes it with no lock held: a create while running (the
    // first table sensor makes the pipeline and starts its run), a start, a tick (registration and
    // delivery), the delivery thread, a stop and a dispose.
    void ReportsReachTheHostWithNoLockHeld()
    {
        {
            // The first table sensor, created while running, with no channel: its run starts at once and
            // says the sensor cannot send.
            Collector collector;
            ReentrantSink sink;
            sink.Attach(collector.value);
            Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
            sink.Expect("create while running", [&] { CreateTable(collector.value, "first", { { "v", HSM_TABLE_COLUMN_DOUBLE } }); });
            Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        }
        {
            // A start that makes a sensor Unavailable, rows lost at stop, a dispose of a running collector.
            Collector collector;
            ReentrantSink sink;
            sink.Attach(collector.value);
            tables::test::InstallChannel(collector.value, std::make_shared<RecordingChannel>());
            tables::test::SetManualTick(collector.value, true);
            Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
            tables::test::InstallChannel(collector.value, nullptr);
            sink.Expect("start", [&] { Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start"); });
            sink.Expect("create in a run", [&] { CreateTable(collector.value, "u", { { "v", HSM_TABLE_COLUMN_DOUBLE } }); });
            Check(AddDouble(table, 1, 1) == HSM_RESULT_INVALID_STATE, "add to an Unavailable sensor");
            sink.Expect("stop", [&] { Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop"); });
            Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start again");
            Check(AddDouble(table, 2, 2) == HSM_RESULT_INVALID_STATE, "add to an Unavailable sensor");
            sink.Expect("dispose", [&] { hsm_collector_dispose(collector.value); });
        }
        {
            // A tick: a registration without an answer, then a block that is not delivered.
            Collector collector;
            ReentrantSink sink;
            sink.Attach(collector.value);
            auto channel = std::make_shared<RecordingChannel>();
            tables::test::InstallChannel(collector.value, channel);
            tables::test::SetManualTick(collector.value, true);
            Table table = CreateTable(collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
            Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
            channel->FailRegistrations(1);
            sink.Expect("tick: registration", [&] { tables::test::RunTick(collector.value); });
            tables::test::RunTick(collector.value);
            Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
            channel->SetDown(true);
            sink.Expect("tick: delivery", [&] { tables::test::RunTick(collector.value); });
            Check(hsm_collector_stop(collector.value) == HSM_RESULT_OK, "stop");
        }
        {
            // The delivery thread.
            ThreadedCollector host;
            ReentrantSink sink;
            sink.Attach(host.collector.value);
            Table table = CreateTable(host.collector.value, "t", { { "v", HSM_TABLE_COLUMN_DOUBLE } });
            Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
            WaitOrFail([&] { return Stats(table).state == HSM_TABLE_SENSOR_REGISTERED; }, "the registration");
            host.channel->SetDown(true);
            Check(AddDouble(table, 1, 1) == HSM_RESULT_OK, "add");
            sink.Expect("delivery thread", [&] {
                const int before = sink.table_reports.load();
                hsm_collector_test_advance_clock_ms(host.collector.value, 1000);
                WaitOrFail([&] { return sink.table_reports.load() > before; }, "a report of the delivery thread");
            });
            Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop");
            CheckEqual(0, sink.failures.load(), "a call back into the collector failed");
        }
    }

    // Three sensors of 100 rows each, registered on a channel whose limits cut them into pieces of 40 rows
    // and whose frame limit makes most blocks split: every allocation point of a pack is met.
    struct PackFixture
    {
        static constexpr int kRows = 100;
        Collector collector;
        std::shared_ptr<RecordingChannel> channel = std::make_shared<RecordingChannel>();
        std::vector<Table> sensors;

        PackFixture()
        {
            tables::TableBlockLimits limits;
            limits.max_rows = 40;
            channel->SetLimits(limits);
            channel->SetMaxBlock(600);
            tables::test::InstallChannel(collector.value, channel);
            tables::test::SetManualTick(collector.value, true);
            for (const char* name : { "a", "b", "c" })
                sensors.push_back(CreateTable(collector.value, name, { { "x", HSM_TABLE_COLUMN_INT64 } }));
            Check(hsm_collector_start(collector.value) == HSM_RESULT_OK, "start");
            tables::test::RunTick(collector.value);
            std::uint64_t seed = 7;
            for (auto& sensor : sensors)
            {
                CheckEqual(static_cast<int>(HSM_TABLE_SENSOR_REGISTERED), static_cast<int>(Stats(sensor).state), "registered");
                std::vector<std::int64_t> times(kRows);
                std::vector<hsm_table_cell_t> cells(kRows);
                for (int r = 0; r < kRows; ++r)
                {
                    seed = seed * 6364136223846793005ULL + 1442695040888963407ULL;
                    times[static_cast<std::size_t>(r)] = r;
                    cells[static_cast<std::size_t>(r)].i64 = static_cast<std::int64_t>(seed);
                }
                Check(hsm_table_sensor_add_rows(sensor.value, times.data(), cells.data(), nullptr, kRows, nullptr) == HSM_RESULT_OK, "add rows");
            }
        }

        tables::TablePipeline& Pipeline() { return *tables::test::Pipeline(collector.value); }

        // Rows of all sensors still in their buffers: accepted, neither delivered nor counted as lost.
        std::int64_t Buffered()
        {
            std::int64_t rows = 0;
            for (auto& sensor : sensors)
            {
                const auto stats = Stats(sensor);
                rows += static_cast<std::int64_t>(stats.rows_accepted) - static_cast<std::int64_t>(channel->RowsOf(hsm_table_sensor_path(sensor.value)).size()) -
                        static_cast<std::int64_t>(stats.rows_dropped);
            }
            return rows;
        }
    };

    // A pack that fails at any of its allocations (the writer, the takes, the pieces, the groups, a
    // split, a block, the backlog, a report) settles every row it took: in a queued block or counted
    // once as "packing failed", on its sensor and in the ledger, with its room given back. The tick's
    // pack and the stop's final pack alike.
    void PackFailureSettlesEveryRow()
    {
        for (const bool at_stop : { false, true })
        {
            int failures = 0;
            for (int fail_at = 0;; ++fail_at)
            {
                PackFixture fixture;
                auto& pipeline = fixture.Pipeline();
                int calls = 0;
                pipeline.on_pack_allocation_for_tests = [&] {
                    if (calls++ == fail_at)
                        throw std::bad_alloc();
                };
                const std::string what = std::string(at_stop ? "stop" : "tick") + ", failure at allocation " + std::to_string(fail_at);
                if (at_stop)
                {
                    Check(hsm_collector_stop(fixture.collector.value) == HSM_RESULT_OK, what + ": stop");
                }
                else
                {
                    tables::test::RunTick(fixture.collector.value);
                    // What the pack did not take is still buffered and holds exactly its room.
                    CheckEqual(fixture.Buffered() * 16, pipeline.ReservedBytes(), what + ": reserved bytes after the failed pack");
                }
                pipeline.on_pack_allocation_for_tests = nullptr;
                const bool failed = calls > fail_at;

                if (!at_stop)
                {
                    tables::test::RunTick(fixture.collector.value);
                    Check(hsm_collector_stop(fixture.collector.value) == HSM_RESULT_OK, what + ": stop");
                }
                CheckEqual(std::int64_t{ 0 }, pipeline.ReservedBytes(), what + ": reserved bytes after stop");
                std::uint64_t dropped = 0;
                for (auto& sensor : fixture.sensors)
                {
                    const auto stats = Stats(sensor);
                    CheckEqual(std::uint64_t{ PackFixture::kRows }, stats.rows_accepted, what + ": accepted");
                    CheckEqual(std::uint64_t{ PackFixture::kRows }, fixture.channel->RowsOf(hsm_table_sensor_path(sensor.value)).size() + stats.rows_dropped,
                               what + ": delivered + dropped");
                    dropped += stats.rows_dropped;
                }
                const auto& ledger = pipeline.Ledger();
                CheckEqual(static_cast<std::int64_t>(dropped),
                           ledger.Total(tables::TableDropReason::PackingFailed) + ledger.Total(tables::TableDropReason::NotDeliveredAtStop),
                           what + ": the ledger has every dropped row");
                if (!failed)
                {
                    CheckEqual(std::uint64_t{ 0 }, dropped, what + ": nothing failed, nothing dropped");
                    break;
                }
                ++failures;
            }
            Check(failures >= 10, "only " + std::to_string(failures) + " allocation points were met");
        }
    }

    // Writer threads against ticks and Stop / Start / Stop on every boundary, a fixed number of rows and
    // runs. Per sensor: offered = accepted + refused at the add call; dropped = refused + lost after
    // acceptance; every accepted row is in exactly one stored block of a run that was accepting while
    // its call ran, or among the sensor's losses; never under another run's producer.
    void StressWritersTicksAndRestarts()
    {
        constexpr int kWriters = 4;
        constexpr int kSensors = 3;
        constexpr int kRowsPerWriter = 6000;
        constexpr int kRuns = 8;

        ThreadedCollector host;
        std::vector<Table> sensors;
        for (int s = 0; s < kSensors; ++s)
            sensors.push_back(CreateTable(host.collector.value, "s" + std::to_string(s), { { "id", HSM_TABLE_COLUMN_INT64 } }));
        auto& pipeline = host.Pipeline();

        std::map<std::int64_t, tables::TableProducerId> producer_of_run;
        std::mutex producers_mutex;

        struct Accepted
        {
            std::int64_t id;
            std::int64_t run_before;
            std::int64_t run_after;
            int sensor;
        };
        std::vector<std::vector<Accepted>> accepted(kWriters);
        std::vector<std::array<std::uint64_t, kSensors>> refused(kWriters);
        std::vector<std::atomic<int>> progress(kWriters);
        for (auto& p : progress)
            p = 0;
        for (auto& r : refused)
            r.fill(0);

        Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
        producer_of_run[pipeline.RunCount()] = pipeline.Producer();

        // The boundaries: per run three ticks, a stop and a start. Boundary b releases chunk b of every
        // writer and is done while the writers write it, so every boundary meets writes in flight; the
        // next boundary waits until every writer has finished its chunk (a wait that only fails).
        constexpr int kBoundaries = kRuns * 5;
        constexpr int kChunk = kRowsPerWriter / kBoundaries;
        static_assert(kChunk * kBoundaries == kRowsPerWriter, "whole chunks");
        std::atomic<int> released{ 0 };

        std::vector<std::thread> writers;
        for (int w = 0; w < kWriters; ++w)
            writers.emplace_back([&, w] {
                for (int i = 0; i < kRowsPerWriter; ++i)
                {
                    if (i % kChunk == 0)
                    {
                        const int chunk = i / kChunk;
                        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(120);
                        while (released.load() <= chunk)
                        {
                            if (std::chrono::steady_clock::now() > deadline)
                                return; // the main thread fails on its own wait
                            std::this_thread::yield();
                        }
                    }
                    const int s = (w + i) % kSensors;
                    hsm_table_cell_t cell;
                    cell.i64 = (static_cast<std::int64_t>(w) << 32) | i;
                    const auto before = pipeline.RunCount();
                    const auto result = hsm_table_sensor_add_row(sensors[static_cast<std::size_t>(s)].value, i, &cell, 1, nullptr);
                    const auto after = pipeline.RunCount();
                    if (result == HSM_RESULT_OK)
                        accepted[static_cast<std::size_t>(w)].push_back({ cell.i64, before, after, s });
                    else
                        ++refused[static_cast<std::size_t>(w)][static_cast<std::size_t>(s)];
                    progress[static_cast<std::size_t>(w)].store(i + 1);
                }
            });

        const auto chunk_done = [&](int chunk) {
            for (int w = 0; w < kWriters; ++w)
                if (progress[static_cast<std::size_t>(w)].load() < (chunk + 1) * kChunk)
                    return false;
            return true;
        };
        int boundary = 0;
        for (int run = 1; run <= kRuns; ++run)
        {
            for (int step = 0; step < 5; ++step, ++boundary)
            {
                released.store(boundary + 1);
                if (step < 3)
                    hsm_collector_test_advance_clock_ms(host.collector.value, 1000);
                else if (step == 3)
                    Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "stop");
                else
                {
                    Check(hsm_collector_start(host.collector.value) == HSM_RESULT_OK, "start");
                    std::lock_guard<std::mutex> guard(producers_mutex);
                    producer_of_run[pipeline.RunCount()] = pipeline.Producer();
                }
                WaitOrFail([&] { return chunk_done(boundary); }, "the writers' chunk " + std::to_string(boundary), 120);
            }
        }
        for (auto& writer : writers)
            writer.join();
        Check(hsm_collector_stop(host.collector.value) == HSM_RESULT_OK, "final stop");
        CheckEqual(std::int64_t{ 0 }, pipeline.ReservedBytes(), "reservations left");

        std::map<tables::TableProducerId, std::int64_t> run_of_producer;
        for (const auto& entry : producer_of_run)
            run_of_producer[entry.second] = entry.first;

        // Delivered rows: id -> run.
        std::map<std::int64_t, std::int64_t> delivered;
        std::array<std::uint64_t, kSensors> delivered_per_sensor{};
        for (const auto& put : host.channel->Stored())
        {
            const auto run = run_of_producer.find(put.producer);
            Check(run != run_of_producer.end(), "a block under an unknown producer");
            const auto names = put.TableNames();
            for (std::size_t t = 0; t < put.block.tables.size(); ++t)
            {
                const int s = std::stoi(names[t].substr(names[t].rfind('s') + 1));
                for (const auto value : put.block.tables[t].columns[1].values)
                {
                    Check(delivered.emplace(static_cast<std::int64_t>(value), run->second).second, "a row delivered twice");
                    ++delivered_per_sensor[static_cast<std::size_t>(s)];
                }
            }
        }

        std::array<std::uint64_t, kSensors> accepted_per_sensor{};
        std::array<std::uint64_t, kSensors> refused_per_sensor{};
        std::size_t accepted_rows = 0;
        for (int w = 0; w < kWriters; ++w)
        {
            for (const auto& row : accepted[static_cast<std::size_t>(w)])
            {
                ++accepted_per_sensor[static_cast<std::size_t>(row.sensor)];
                ++accepted_rows;
                const auto it = delivered.find(row.id);
                if (it == delivered.end())
                    continue;
                Check(it->second >= row.run_before && it->second <= row.run_after,
                      "a row of runs " + std::to_string(row.run_before) + ".." + std::to_string(row.run_after) + " under run " + std::to_string(it->second));
                delivered.erase(it);
            }
            for (int s = 0; s < kSensors; ++s)
                refused_per_sensor[static_cast<std::size_t>(s)] += refused[static_cast<std::size_t>(w)][static_cast<std::size_t>(s)];
        }
        Check(delivered.empty(), std::to_string(delivered.size()) + " delivered row(s) were never accepted");

        std::uint64_t offered = 0;
        for (int s = 0; s < kSensors; ++s)
        {
            const auto stats = Stats(sensors[static_cast<std::size_t>(s)]);
            const auto si = static_cast<std::size_t>(s);
            CheckEqual(accepted_per_sensor[si], stats.rows_accepted, "accepted by sensor " + std::to_string(s));
            CheckEqual(refused_per_sensor[si] + (accepted_per_sensor[si] - delivered_per_sensor[si]), stats.rows_dropped,
                       "dropped by sensor " + std::to_string(s));
            offered += accepted_per_sensor[si] + refused_per_sensor[si];
        }
        CheckEqual(static_cast<std::uint64_t>(kWriters) * kRowsPerWriter, offered, "offered");
        Check(accepted_rows > 0, "no row was accepted");
    }
} // namespace
#endif

const std::map<std::string, std::function<void(const std::string&)>>& TablePipelineTests()
{
    static const std::map<std::string, std::function<void(const std::string&)>> tests = {
        { "native_table_path_normalization", [](const std::string&) { PathNormalization(); } },
        { "native_table_time_conversion", [](const std::string&) { TimeConversion(); } },
        { "native_table_buffer_growth_and_swap", [](const std::string&) { BufferGrowthAndSwap(); } },
        { "native_table_buffer_failed_growth", [](const std::string&) { BufferFailedGrowth(); } },
        { "native_table_buffer_null_map_on_first_use", [](const std::string&) { BufferNullMapOnFirstUse(); } },
        { "native_table_bound_never_overshoots", [](const std::string&) { BoundNeverOvershoots(); } },
        { "native_table_ledger_counts_by_reason", [](const std::string&) { LedgerCountsByReason(); } },
        { "native_table_backlog_keeps_block_in_flight", [](const std::string&) { BacklogKeepsBlockInFlight(); } },
        { "native_table_abi_rejects_wrong_shapes", [](const std::string&) { AbiRejectsWrongShapes(); } },
        { "native_table_unavailable_says_why", [](const std::string&) { UnavailableSaysWhy(); } },
        { "native_table_host_without_table_sensors", [](const std::string&) { HostWithoutTableSensors(); } },
        { "native_table_handle_outlives_collector", [](const std::string&) { HandleOutlivesCollector(); } },
        { "native_table_wrapper_matches_abi", [](const std::string&) { WrapperMatchesAbi(); } },
#if defined(HSM_COLLECTOR_TABLES)
        { "native_table_threads_only_while_running", [](const std::string&) { ThreadsOnlyWhileRunning(); } },
        { "native_table_unavailable_decided_at_each_start", [](const std::string&) { UnavailableDecidedAtEachStart(); } },
        { "native_table_writer_across_stop_and_start", [](const std::string&) { WriterAcrossStopAndStart(); } },
        { "native_table_stop_drain_is_bounded", [](const std::string&) { StopDrainIsBounded(); } },
        { "native_table_losses_at_stop_reach_the_stop_line", [](const std::string&) { LossesAtStopReachTheStopLine(); } },
        { "native_table_split_keeps_every_row", [](const std::string&) { SplitKeepsEveryRow(); } },
        { "native_table_too_large_is_final_and_sequences_never_go_back", [](const std::string&) { TooLargeIsFinalAndSequencesNeverGoBack(); } },
        { "native_table_over_connection_limits", [](const std::string&) { TableOverConnectionLimits(); } },
        { "native_table_impossible_row_count_is_a_wrong_shape", [](const std::string&) { ImpossibleRowCountIsAWrongShape(); } },
        { "native_table_start_failure_rolls_back", [](const std::string&) { StartFailureRollsBack(); } },
        { "native_table_stop_failure_completes", [](const std::string&) { StopFailureCompletes(); } },
        { "native_table_reports_reach_the_host_with_no_lock_held", [](const std::string&) { ReportsReachTheHostWithNoLockHeld(); } },
        { "native_table_pack_failure_settles_every_row", [](const std::string&) { PackFailureSettlesEveryRow(); } },
        { "native_table_stress_writers_ticks_and_restarts", [](const std::string&) { StressWritersTicksAndRestarts(); } },
#endif
    };
    return tests;
}
