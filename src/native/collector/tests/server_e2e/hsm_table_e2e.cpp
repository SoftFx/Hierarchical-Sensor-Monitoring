// The native collector's table sensors against a real HSM server (#1848 step 6).
//
// A thin driver over the C++ API (hsm_collector/tables.hpp): it reads commands from stdin, one per
// line, and answers each with one line, so a test (NativeTableSensorEndToEndTests in
// src/collector/HSMDataCollector.IntegrationTests) or a person can run collectors against a server and
// read the rows back from the server itself. It decides nothing: every check is the caller's.
//
//   hsm_table_e2e <server address> <port>     the access key is read from HSM_E2E_KEY
//
// Commands (words separated by one space):
//   collector <c> session|http <client name>   a collector named <c>, untrusted certificate allowed,
//                                              table tick 1 s, its log lines written as "log <c> ..."
//   table <c> <t> <path> <name>:double|int64[,...]   a table sensor <t> of collector <c> at the
//                                              product's root
//   start <c> | stop <c> | dispose <c>
//   rows <t> <base ns> <first> <count>         rows first..first+count-1, one AddRow each
//   batch <t> <base ns> <first> <count>        the same rows in one AddRows (all or nothing)
//   stats <t>                                  "= ok <state> <accepted> <dropped> <reason>"
//   quit
// An answer is "= ok ..." or "= error <text>".
//
// The rows (the test computes the same): row r has the time base + r ms + (r % 7) * 100 ns + 87 ns,
// kept by the collector to 100 ns (rounded down: 87 ns tells it from rounding to the nearest); its
// cell of value column c is empty when (r + c) % 7 == 3, else c + 1.0841 + r / 100000.0 for a double
// column and r * 1000003 - 7 + c for an int64 column.

#include <hsm_collector/hsm_collector.hpp>

#include <cstdint>
#include <cstdlib>
#include <exception>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace hc = hsm::collector;

namespace
{
    std::mutex output_mutex;

    void Say(const std::string& line)
    {
        std::lock_guard<std::mutex> lock(output_mutex);
        std::cout << line << '\n'
                  << std::flush;
    }

    std::string OneLine(std::string text)
    {
        for (auto& c : text)
            if (c == '\r' || c == '\n')
                c = ' ';
        return text;
    }

    const char* StateName(hc::TableSensorState state)
    {
        switch (state)
        {
        case hc::TableSensorState::Pending:
            return "Pending";
        case hc::TableSensorState::Registered:
            return "Registered";
        case hc::TableSensorState::Refused:
            return "Refused";
        case hc::TableSensorState::Unavailable:
            return "Unavailable";
        }
        return "Unknown";
    }

    const char* LevelName(hc::LogLevel level)
    {
        switch (level)
        {
        case hc::LogLevel::Debug:
            return "debug";
        case hc::LogLevel::Info:
            return "info";
        case hc::LogLevel::Error:
            return "error";
        }
        return "unknown";
    }

    std::vector<std::string> Words(const std::string& line)
    {
        std::vector<std::string> words;
        std::istringstream input(line);
        std::string word;
        while (input >> word)
            words.push_back(word);
        return words;
    }

    std::int64_t RowTime(std::int64_t base_ns, std::int64_t row)
    {
        return base_ns + row * 1000000 + (row % 7) * 100 + 87;
    }

    bool IsNull(std::int64_t row, std::size_t column)
    {
        return (row + static_cast<std::int64_t>(column)) % 7 == 3;
    }

    double DoubleCell(std::int64_t row, std::size_t column)
    {
        return (static_cast<double>(column) + 1.0841) + static_cast<double>(row) / 100000.0;
    }

    std::int64_t Int64Cell(std::int64_t row, std::size_t column)
    {
        return row * 1000003 - 7 + static_cast<std::int64_t>(column);
    }

    struct Table
    {
        hc::TableSensor sensor;
        std::vector<hc::TableColumnType> types;
    };

    class Driver
    {
    public:
        Driver(std::string address, int port, std::string key)
            : address_(std::move(address)), port_(port), key_(std::move(key))
        {
        }

        // Tables first: a handle may outlive its collector, but not the other way round in this driver.
        ~Driver()
        {
            tables_.clear();
            collectors_.clear();
        }

        std::string Run(const std::vector<std::string>& words)
        {
            const std::string& verb = words[0];
            if (verb == "collector" && words.size() == 4)
                return MakeCollector(words[1], words[2], words[3]);
            if (verb == "table" && words.size() == 5)
                return MakeTable(words[1], words[2], words[3], words[4]);
            if (verb == "start" && words.size() == 2)
                return Lifecycle(words[1], [](hc::Collector& c) { c.Start(); });
            if (verb == "stop" && words.size() == 2)
                return Lifecycle(words[1], [](hc::Collector& c) { c.Stop(); });
            if (verb == "dispose" && words.size() == 2)
                return Lifecycle(words[1], [](hc::Collector& c) { c.Dispose(); });
            if ((verb == "rows" || verb == "batch") && words.size() == 5)
                return AddRows(verb == "batch", words[1], std::stoll(words[2]), std::stoll(words[3]), std::stoll(words[4]));
            if (verb == "stats" && words.size() == 2)
                return Stats(words[1]);
            return "= error unknown command";
        }

    private:
        std::string MakeCollector(const std::string& name, const std::string& transport, const std::string& client)
        {
            if (collectors_.count(name) != 0)
                return "= error the collector exists";

            hc::CollectorOptions options;
            options.access_key = key_;
            options.server_address = address_;
            options.port = port_;
            options.client_name = client;
            options.computer_name = "e2e";
            options.module = "native";
            options.allow_untrusted_server_certificate = true; // the test server's self-signed certificate

            auto collector = std::make_unique<hc::Collector>(options);
            collector->SetLogger([name](hc::LogLevel level, const std::string& text) { Say("log " + name + " " + LevelName(level) + " " + OneLine(text)); });

            if (transport == "session")
                collector->UseSessionTransport();
            else if (transport == "http")
                collector->UseHttpTransport();
            else
                return "= error the transport is session or http";

            hc::TableOptions tables;
            tables.tick_period = std::chrono::seconds(1);
            collector->SetTableOptions(tables);

            collectors_[name] = std::move(collector);
            return "= ok";
        }

        std::string MakeTable(const std::string& collector, const std::string& name, const std::string& path, const std::string& columns)
        {
            const auto found = collectors_.find(collector);
            if (found == collectors_.end())
                return "= error no collector " + collector;
            if (tables_.count(name) != 0)
                return "= error the table exists";

            hc::TableSensorOptions options;
            options.location = hc::SensorLocation::Product;
            options.description = "native end-to-end table sensor";

            std::vector<hc::TableColumnType> types;
            std::istringstream list(columns);
            std::string column;
            while (std::getline(list, column, ','))
            {
                const auto colon = column.find(':');
                if (colon == std::string::npos)
                    return "= error a column is <name>:double|int64";
                const std::string type = column.substr(colon + 1);
                if (type == "double")
                    options.columns.push_back(hc::TableColumn::Double(column.substr(0, colon)));
                else if (type == "int64")
                    options.columns.push_back(hc::TableColumn::Int64(column.substr(0, colon)));
                else
                    return "= error a column type is double or int64";
                types.push_back(options.columns.back().type);
            }

            auto sensor = found->second->CreateTableSensor(path, options);
            const std::string normalized = sensor.Path();
            tables_[name] = std::make_unique<Table>(Table{ std::move(sensor), std::move(types) });
            return "= ok " + normalized;
        }

        template <typename Action>
        std::string Lifecycle(const std::string& name, Action action)
        {
            const auto found = collectors_.find(name);
            if (found == collectors_.end())
                return "= error no collector " + name;
            action(*found->second);
            return "= ok";
        }

        std::string AddRows(bool batch, const std::string& name, std::int64_t base_ns, std::int64_t first, std::int64_t count)
        {
            const auto found = tables_.find(name);
            if (found == tables_.end())
                return "= error no table " + name;
            if (count < 0 || first < 0)
                return "= error a negative row";

            Table& table = *found->second;
            const std::size_t columns = table.types.size();

            if (!batch)
            {
                std::int64_t taken = 0;
                std::vector<hc::TableCell> cells;
                for (std::int64_t row = first; row < first + count; ++row)
                {
                    cells.clear();
                    for (std::size_t c = 0; c < columns; ++c)
                    {
                        if (IsNull(row, c))
                            cells.push_back(hc::TableCell::Null());
                        else if (table.types[c] == hc::TableColumnType::Double)
                            cells.emplace_back(DoubleCell(row, c));
                        else
                            cells.emplace_back(Int64Cell(row, c));
                    }
                    if (table.sensor.AddRow(RowTime(base_ns, row), cells.data(), cells.size()))
                        ++taken;
                }
                return "= ok " + std::to_string(taken);
            }

            const auto rows = static_cast<std::size_t>(count);
            std::vector<std::int64_t> times(rows);
            std::vector<hsm_table_cell_t> cells(rows * columns);
            std::vector<std::uint8_t> nulls((rows * columns + 7) / 8, 0);
            for (std::size_t r = 0; r < rows; ++r)
            {
                const std::int64_t row = first + static_cast<std::int64_t>(r);
                times[r] = RowTime(base_ns, row);
                for (std::size_t c = 0; c < columns; ++c)
                {
                    const std::size_t bit = r * columns + c;
                    if (IsNull(row, c))
                        nulls[bit / 8] = static_cast<std::uint8_t>(nulls[bit / 8] | (1u << (bit % 8)));
                    else if (table.types[c] == hc::TableColumnType::Double)
                        cells[bit].f64 = DoubleCell(row, c);
                    else
                        cells[bit].i64 = Int64Cell(row, c);
                }
            }
            return "= ok " + std::to_string(table.sensor.AddRows(times.data(), cells.data(), nulls.data(), rows));
        }

        std::string Stats(const std::string& name) const
        {
            const auto found = tables_.find(name);
            if (found == tables_.end())
                return "= error no table " + name;
            const auto stats = found->second->sensor.Stats();
            return "= ok " + std::string(StateName(stats.state)) + " " + std::to_string(stats.rows_accepted) + " " +
                   std::to_string(stats.rows_dropped) + " " + OneLine(stats.reason);
        }

        std::string address_;
        int port_;
        std::string key_;
        std::map<std::string, std::unique_ptr<hc::Collector>> collectors_;
        std::map<std::string, std::unique_ptr<Table>> tables_;
    };
} // namespace

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "usage: hsm_table_e2e <server address> <port> (the access key in HSM_E2E_KEY; commands on stdin)\n";
        return 2;
    }

    std::string key;
#ifdef _MSC_VER
    char* env = nullptr;
    std::size_t env_length = 0;
    if (_dupenv_s(&env, &env_length, "HSM_E2E_KEY") == 0 && env != nullptr)
    {
        key = env;
        std::free(env);
    }
#else
    if (const char* env = std::getenv("HSM_E2E_KEY"))
        key = env;
#endif
    if (key.empty())
    {
        std::cerr << "hsm_table_e2e: HSM_E2E_KEY is not set\n";
        return 2;
    }

    Driver driver(argv[1], std::atoi(argv[2]), key);
    Say("= ready " + std::string(HSM_COLLECTOR_VERSION_STRING));

    std::string line;
    while (std::getline(std::cin, line))
    {
        const auto words = Words(line);
        if (words.empty())
            continue;
        if (words[0] == "quit")
            break;

        try
        {
            Say(driver.Run(words));
        }
        catch (const std::exception& error)
        {
            Say("= error " + OneLine(error.what()));
        }
    }

    Say("= bye");
    return 0;
}
