#pragma once

/// @file
/// @brief Table sensors in the C++ wrapper (since 0.15.0, #1848): a thin RAII layer over the
/// hsm_table_sensor_* functions of the C ABI. Create one with Collector::CreateTableSensor.

#include "hsm_collector/error.hpp"
#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/options.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace hsm::collector
{
    enum class TableColumnType
    {
        Int64 = HSM_TABLE_COLUMN_INT64,
        Double = HSM_TABLE_COLUMN_DOUBLE,
    };

    /// A value column. The time column is implicit and first, named "time".
    struct TableColumn
    {
        std::string name;
        TableColumnType type = TableColumnType::Double;

        static TableColumn Double(std::string name) { return TableColumn{ std::move(name), TableColumnType::Double }; }
        static TableColumn Int64(std::string name) { return TableColumn{ std::move(name), TableColumnType::Int64 }; }
    };

    struct TableSensorOptions
    {
        std::vector<TableColumn> columns;
        std::string description;
        /// Zero: the server's default.
        std::chrono::milliseconds keep_history{ 0 };
        SensorLocation location = SensorLocation::Module;
    };

    /// Collector-wide table settings (hsm_table_options_t). Zero takes the default.
    struct TableOptions
    {
        std::chrono::milliseconds tick_period{ 0 }; ///< [10 s], at least 1 s
        std::int64_t max_buffer_bytes = 0;          ///< [64 MiB]
        std::int64_t max_backlog_bytes = 0;         ///< [64 MiB]
    };

    /// One cell of a row: a double, a 64-bit integer, or no value. An integer in a double column is
    /// converted; a double in an integer column is a wrong shape (AddRow throws).
    class TableCell
    {
    public:
        TableCell(double value) noexcept
            : kind_(Kind::Double)
        {
            cell_.f64 = value;
        }
        TableCell(std::int64_t value) noexcept
            : kind_(Kind::Int64)
        {
            cell_.i64 = value;
        }
        TableCell(int value) noexcept
            : TableCell(static_cast<std::int64_t>(value))
        {
        }
        TableCell(std::optional<double> value) noexcept
            : TableCell(value.has_value() ? TableCell(*value) : Null())
        {
        }
        TableCell(std::optional<std::int64_t> value) noexcept
            : TableCell(value.has_value() ? TableCell(*value) : Null())
        {
        }

        /// A cell without a value.
        static TableCell Null() noexcept { return TableCell(); }

        bool IsNull() const noexcept { return kind_ == Kind::None; }
        bool IsDouble() const noexcept { return kind_ == Kind::Double; }
        bool IsInt64() const noexcept { return kind_ == Kind::Int64; }
        double AsDouble() const noexcept { return cell_.f64; }
        std::int64_t AsInt64() const noexcept { return cell_.i64; }

    private:
        enum class Kind
        {
            None,
            Double,
            Int64,
        };

        TableCell() noexcept
            : kind_(Kind::None)
        {
            cell_.i64 = 0;
        }

        Kind kind_;
        hsm_table_cell_t cell_;
    };

    enum class TableSensorState
    {
        Pending = HSM_TABLE_SENSOR_PENDING,
        Registered = HSM_TABLE_SENSOR_REGISTERED,
        Refused = HSM_TABLE_SENSOR_REFUSED,
        Unavailable = HSM_TABLE_SENSOR_UNAVAILABLE,
    };

    struct TableSensorStats
    {
        TableSensorState state = TableSensorState::Pending;
        /// Why the sensor is Refused or Unavailable; empty otherwise.
        std::string reason;
        std::uint64_t rows_accepted = 0;
        std::uint64_t rows_dropped = 0;
    };

    /// Owning, move-only handle to a table sensor. Rows may be added from any thread. A dropped row
    /// (the collector is not running, the buffer is full, the sensor cannot send) is counted and
    /// reported by the collector and returns false / 0; only a row of the wrong shape throws.
    class TableSensor
    {
    public:
        TableSensor() = default;

        TableSensor(hsm_table_sensor_t* handle, std::vector<TableColumnType> types) noexcept
            : handle_(handle), types_(std::move(types))
        {
        }

        TableSensor(const TableSensor&) = delete;
        TableSensor& operator=(const TableSensor&) = delete;

        TableSensor(TableSensor&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr)), types_(std::move(other.types_))
        {
        }

        TableSensor& operator=(TableSensor&& other) noexcept
        {
            if (this != &other)
            {
                hsm_table_sensor_release(handle_);
                handle_ = std::exchange(other.handle_, nullptr);
                types_ = std::move(other.types_);
            }
            return *this;
        }

        ~TableSensor() { hsm_table_sensor_release(handle_); }

        /// Adds one row; `time` is kept to 100 ns. False when the row was dropped (and counted).
        bool AddRow(std::chrono::system_clock::time_point time, std::initializer_list<TableCell> cells)
        {
            return AddRow(UnixNanoseconds(time), cells.begin(), cells.size());
        }

        bool AddRow(std::int64_t time_unix_ns, const TableCell* cells, std::size_t count)
        {
            Require(handle_ != nullptr, "The table sensor handle is empty.");
            if (count != types_.size())
                throw Error("A row has " + std::to_string(types_.size()) + " value column(s); " + std::to_string(count) +
                            " cell(s) were given.");

            std::vector<hsm_table_cell_t> raw(count);
            std::vector<std::uint8_t> nulls((count + 7) / 8, 0);
            const bool any_null = Convert(cells, count, raw.data(), nulls.data(), 0);
            const auto result = hsm_table_sensor_add_row(handle_, time_unix_ns, raw.data(), count, any_null ? nulls.data() : nullptr);
            if (result == HSM_RESULT_INVALID_ARGUMENT)
                detail::ThrowIfFailed(result, "AddRow refused the row.");
            return result == HSM_RESULT_OK;
        }

        /// Adds `rows` rows at once, all or nothing, in the C ABI's own form (row-major cells, a null
        /// bit map or nullptr). Returns `rows` or 0.
        std::size_t AddRows(const std::int64_t* times_unix_ns, const hsm_table_cell_t* cells, const std::uint8_t* nulls, std::size_t rows)
        {
            Require(handle_ != nullptr, "The table sensor handle is empty.");
            std::size_t taken = 0;
            const auto result = hsm_table_sensor_add_rows(handle_, times_unix_ns, cells, nulls, rows, &taken);
            if (result == HSM_RESULT_INVALID_ARGUMENT)
                detail::ThrowIfFailed(result, "AddRows refused the rows.");
            return taken;
        }

        /// Adds rows given as times and row-major TableCells. Returns times.size() or 0.
        std::size_t AddRows(const std::vector<std::chrono::system_clock::time_point>& times, const std::vector<TableCell>& cells)
        {
            Require(handle_ != nullptr, "The table sensor handle is empty.");
            const std::size_t columns = types_.size();
            if (cells.size() != times.size() * columns)
                throw Error(std::to_string(times.size()) + " row(s) of " + std::to_string(columns) + " column(s) need " +
                            std::to_string(times.size() * columns) + " cells; " + std::to_string(cells.size()) + " were given.");
            if (times.empty())
                return 0;

            std::vector<std::int64_t> ns(times.size());
            for (std::size_t row = 0; row < times.size(); ++row)
                ns[row] = UnixNanoseconds(times[row]);
            std::vector<hsm_table_cell_t> raw(cells.size());
            std::vector<std::uint8_t> nulls((cells.size() + 7) / 8, 0);
            bool any_null = false;
            for (std::size_t row = 0; row < times.size(); ++row)
                any_null = Convert(cells.data() + row * columns, columns, raw.data() + row * columns, nulls.data(), row * columns) || any_null;
            return AddRows(ns.data(), raw.data(), any_null ? nulls.data() : nullptr, times.size());
        }

        TableSensorStats Stats() const
        {
            TableSensorStats stats;
            if (handle_ == nullptr)
                return stats;
            hsm_table_sensor_stats_t raw{};
            raw.struct_size = sizeof raw;
            if (hsm_table_sensor_get_stats(handle_, &raw) == HSM_RESULT_OK)
            {
                stats.state = static_cast<TableSensorState>(raw.state);
                stats.rows_accepted = raw.rows_accepted;
                stats.rows_dropped = raw.rows_dropped;
            }
            const std::size_t length = hsm_table_sensor_state_reason(handle_, nullptr, 0);
            if (length > 0)
            {
                std::string reason(length + 1, '\0');
                hsm_table_sensor_state_reason(handle_, reason.data(), reason.size());
                reason.resize(length);
                stats.reason = std::move(reason);
            }
            return stats;
        }

        /// The normalized full path the sensor is registered and sent under.
        std::string Path() const
        {
            const char* path = hsm_table_sensor_path(handle_);
            return path == nullptr ? std::string{} : std::string{ path };
        }

        /// Borrowed C handle (still owned by this object).
        hsm_table_sensor_t* handle() const noexcept { return handle_; }

    private:
        static std::int64_t UnixNanoseconds(std::chrono::system_clock::time_point time)
        {
            return std::chrono::duration_cast<std::chrono::nanoseconds>(time.time_since_epoch()).count();
        }

        static void Require(bool condition, const char* message)
        {
            if (!condition)
                throw Error(message);
        }

        // Writes the cells of one row; true when one of them has no value (its bit set at `first_bit`).
        bool Convert(const TableCell* cells, std::size_t count, hsm_table_cell_t* out, std::uint8_t* nulls, std::size_t first_bit) const
        {
            bool any_null = false;
            for (std::size_t column = 0; column < count; ++column)
            {
                const TableCell& cell = cells[column];
                const std::size_t bit = first_bit + column;
                if (cell.IsNull())
                {
                    out[column].i64 = 0;
                    nulls[bit / 8] = static_cast<std::uint8_t>(nulls[bit / 8] | (1u << (bit % 8)));
                    any_null = true;
                }
                else if (types_[column] == TableColumnType::Int64)
                {
                    if (cell.IsDouble())
                        throw Error("Column " + std::to_string(column) + " holds 64-bit integers; a double was given.");
                    out[column].i64 = cell.AsInt64();
                }
                else
                {
                    out[column].f64 = cell.IsDouble() ? cell.AsDouble() : static_cast<double>(cell.AsInt64());
                }
            }
            return any_null;
        }

        hsm_table_sensor_t* handle_ = nullptr;
        std::vector<TableColumnType> types_;
    };
} // namespace hsm::collector
