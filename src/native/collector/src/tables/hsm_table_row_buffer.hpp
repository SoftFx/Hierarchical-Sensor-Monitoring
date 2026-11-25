#pragma once

/// @file
/// @brief The rows of one table sensor between two ticks, column by column (#1848). Internal; the
/// managed counterpart is HSMDataCollector.Tables.TableRowBuffer.

#include <cstddef>
#include <cstdint>
#include <functional>
#include <vector>

namespace hsm::collector::tables
{
    /// DateTime ticks (100 ns) of 1970-01-01 UTC.
    constexpr std::int64_t kUnixEpochTicks = 621355968000000000LL;

    /// The block's time of a Unix time in nanoseconds: 100 ns ticks since 0001-01-01, rounded down.
    /// Every int64 nanosecond count lies inside the format's time range.
    inline std::uint64_t TicksFromUnixNanoseconds(std::int64_t unix_ns) noexcept
    {
        std::int64_t quotient = unix_ns / 100;
        if (unix_ns % 100 < 0)
            --quotient;
        return static_cast<std::uint64_t>(quotient + kUnixEpochTicks);
    }

    /// The times (block ticks), every value column as 64-bit words (a double as its bits), and a null
    /// map for a column only once a row of it has no value. Not thread-safe: its sensor appends under
    /// its own mutex, and a tick reads a buffer it has taken out of the sensor.
    class TableRowBuffer
    {
    public:
        static constexpr std::size_t kInitialCapacity = 64;

        /// The capacity in rows a cleared buffer keeps. A buffer that grew past it gives its arrays up
        /// when it is cleared, so a quiet sensor keeps at most two buffers of 256 rows (4 KiB per
        /// column, the time included); the bytes of a busy sensor's rows are bounded by the collector's
        /// max_buffer_bytes until they are packed.
        static constexpr std::size_t kRetainedCapacity = 256;

        explicit TableRowBuffer(std::size_t columns);

        TableRowBuffer(const TableRowBuffer&) = delete;
        TableRowBuffer& operator=(const TableRowBuffer&) = delete;

        std::size_t Count() const noexcept { return count_; }
        std::size_t ColumnCount() const noexcept { return columns_; }
        std::size_t Capacity() const noexcept { return times_.size(); }

        const std::uint64_t* Times() const noexcept { return times_.data(); }
        const std::uint64_t* Values(std::size_t column) const noexcept { return values_[column].data(); }
        bool HasNulls(std::size_t column) const noexcept { return has_nulls_[column]; }
        bool IsNull(std::size_t column, std::size_t row) const noexcept;

        /// Appends `rows` rows: `times_unix_ns[r]` is row r's time (kept as block ticks), `cells` is
        /// row-major (rows x columns of 8 bytes, each a 64-bit pattern, read by memcpy), `nulls` is null or
        /// a bit map in which bit r * columns + c marks a cell without a value. All or nothing: the only failure is std::bad_alloc while the buffer grows, and it
        /// leaves the buffer exactly as it was (every new array is allocated before any is swapped in).
        void Append(const std::int64_t* times_unix_ns, const void* cells, const std::uint8_t* nulls, std::size_t rows);

        /// Forgets the rows. A buffer within kRetainedCapacity keeps its arrays; a larger one gives them up.
        void Clear() noexcept;

        /// Writes the null map of rows [start, start + count) of a column into `map` (ceil(count / 8)
        /// bytes, all zero on entry). False, and `map` untouched, when none of these rows is null.
        bool CopyNullMap(std::size_t column, std::size_t start, std::size_t count, std::uint8_t* map) const noexcept;

        /// For tests: runs before the arrays of a growth are allocated (a test makes it throw bad_alloc).
        std::function<void()> before_growth;

    private:
        std::size_t columns_;
        std::size_t count_ = 0;
        std::vector<std::uint64_t> times_;
        std::vector<std::vector<std::uint64_t>> values_;
        std::vector<std::vector<std::uint8_t>> nulls_;
        std::vector<bool> has_nulls_;
    };
} // namespace hsm::collector::tables
