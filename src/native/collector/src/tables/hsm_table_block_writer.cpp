#include "hsm_table_block_writer.hpp"

#include <zlib.h>

#include <algorithm>
#include <cstring>
#include <limits>
#include <new>

// The format stores doubles by their IEEE 754 binary64 bits; a platform with another double cannot
// write it by copying.
static_assert(std::numeric_limits<double>::is_iec559 && sizeof(double) == 8,
              "table blocks need double to be IEEE 754 binary64");
static_assert(sizeof(std::uint64_t) == 8, "table block values are 8 bytes");

namespace hsm::collector::tables
{
    namespace
    {
#if defined(_MSC_VER) || (defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_LITTLE_ENDIAN__)
        constexpr bool kLittleEndian = true;
#elif defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
        constexpr bool kLittleEndian = false;
#else
#error "unknown byte order: the table block writer needs to know whether to swap"
#endif

        // Every header and directory field is stored byte by byte: no cast into the buffer, no
        // alignment assumption.
        void StoreLe(std::uint8_t* at, std::uint64_t value, std::size_t width) noexcept
        {
            for (std::size_t i = 0; i < width; ++i)
                at[i] = static_cast<std::uint8_t>(value >> (8 * i));
        }

        std::uint64_t LoadValue(const std::uint8_t* at) noexcept
        {
            std::uint64_t value;
            std::memcpy(&value, at, sizeof value);
            return value;
        }

        std::size_t MapLength(std::int64_t rows) noexcept
        {
            return static_cast<std::size_t>((rows + 7) / 8);
        }

        bool IsNull(const std::uint8_t* map, std::size_t row) noexcept
        {
            return (map[row >> 3] & (1u << (row & 7))) != 0;
        }

        bool KnownType(TableColumnType type) noexcept
        {
            return type == TableColumnType::Time || type == TableColumnType::Int64 || type == TableColumnType::Double;
        }
    } // namespace

    std::uint32_t TableBlockCrc32(const std::uint8_t* data, std::size_t size, std::uint32_t crc) noexcept
    {
        return static_cast<std::uint32_t>(crc32_z(crc, data, size));
    }

    void CopyColumnLe(std::uint8_t* destination, const std::uint8_t* source, std::size_t count,
                      const std::uint8_t* nulls, bool swap) noexcept
    {
        constexpr std::size_t kSize = TableBlockFormat::kValueSize;
        if (nulls == nullptr && !swap)
        {
            std::memcpy(destination, source, count * kSize);
            return;
        }

        for (std::size_t row = 0; row < count; ++row)
        {
            std::uint8_t* to = destination + row * kSize;
            // A null cell is decided by the map first: its source is never read.
            if (nulls != nullptr && IsNull(nulls, row))
            {
                std::memset(to, 0, kSize);
                continue;
            }

            const std::uint8_t* from = source + row * kSize;
            if (swap)
            {
                for (std::size_t k = 0; k < kSize; ++k)
                    to[k] = from[kSize - 1 - k];
            }
            else
            {
                std::memcpy(to, from, kSize);
            }
        }
    }

    TableBlockWriter::TableBlockWriter(const TableBlockLimits& limits, int level)
        : limits_(limits), limits_valid_(limits.Valid()), compressor_(level)
    {
    }

    std::int64_t TableBlockWriter::DecompressedLength() const noexcept
    {
        return static_cast<std::int64_t>(directory_.size() + data_.size() + null_data_.size());
    }

    void TableBlockWriter::Reset() noexcept
    {
        directory_.clear();
        data_.clear();
        null_data_.clear();
        column_maps_.clear();
        tables_.clear();
        row_count_ = 0;
        first_time_ = 0;
        last_time_ = 0;
        max_columns_ = 0;
        column_count_offset_ = -1;
        failed_ = false;
        error_ = "";
    }

    TableBlockStatus TableBlockWriter::Fail(TableBlockStatus status, const char* message) noexcept
    {
        error_ = message;
        return status;
    }

    template <typename T>
    void TableBlockWriter::Reserve(std::vector<T>& buffer, std::size_t needed)
    {
        if (needed <= buffer.capacity())
            return;

        if (fail_allocation_after_ == 0)
            throw std::bad_alloc();
        if (fail_allocation_after_ > 0)
            --fail_allocation_after_;

        // Geometric growth: one reallocation per doubling, whatever the order of the calls.
        const std::size_t doubled = buffer.capacity() <= buffer.max_size() / 2 ? buffer.capacity() * 2 : buffer.max_size();
        buffer.reserve(std::max(needed, doubled));
        ++reallocations_;
    }

    TableBlockStatus TableBlockWriter::CheckGrowth(std::uint64_t bytes) noexcept
    {
        // The writer never holds more than the limit, so the room left is not negative.
        const std::uint64_t room = static_cast<std::uint64_t>(limits_.max_decompressed_bytes - DecompressedLength());
        if (bytes > room)
            return Fail(TableBlockStatus::LimitExceeded, "the payload would exceed the decompressed length limit: send the block and start a new one");
        return TableBlockStatus::Ok;
    }

    TableBlockStatus TableBlockWriter::BeginTable(const std::uint8_t* id, std::size_t id_length, std::uint32_t schema_id, std::int64_t row_count) noexcept
    {
        if (failed_)
            return Fail(TableBlockStatus::OutOfMemory, "the writer ran out of memory: Reset it first");
        if (!limits_valid_)
            return Fail(TableBlockStatus::InvalidArgument, "the writer's limits are outside the format");
        if (column_count_offset_ >= 0 && tables_.back().columns == 0)
            return Fail(TableBlockStatus::WrongOrder, "the open table has no columns: add its time column first");
        if (id == nullptr || id_length < 1)
            return Fail(TableBlockStatus::InvalidArgument, "a table identifier has at least one byte");
        if (id_length > static_cast<std::size_t>(limits_.max_table_id_length))
            return Fail(TableBlockStatus::LimitExceeded, "the table identifier is longer than the limit");
        if (row_count < 1)
            return Fail(TableBlockStatus::InvalidArgument, "a table has at least one row");
        if (tables_.size() >= static_cast<std::size_t>(limits_.max_tables))
            return Fail(TableBlockStatus::LimitExceeded, "the block would hold more tables than the limit");
        // Against the room left, never as a sum: 0 <= row_count_ <= max_rows always holds. max_rows is
        // an int32, so this also keeps every row count inside the format's int32.
        if (row_count > limits_.max_rows - row_count_)
            return Fail(TableBlockStatus::LimitExceeded, "the block would hold more rows than the limit");

        // At most 65,535 + 11 and 2^31 * 8: no overflow in 64 bits.
        const std::size_t entry_length = TableBlockFormat::kEntryFixedSize + id_length;
        // The entry, its first column's type byte and the first column's values.
        const TableBlockStatus growth = CheckGrowth(static_cast<std::uint64_t>(entry_length) + 1 +
                                                    static_cast<std::uint64_t>(row_count) * TableBlockFormat::kValueSize);
        if (growth != TableBlockStatus::Ok)
            return growth;

        // Every allocation first; nothing below can throw once they have succeeded.
        try
        {
            Reserve(tables_, tables_.size() + 1);
            Reserve(directory_, directory_.size() + entry_length);
        }
        catch (...)
        {
            failed_ = true;
            return Fail(TableBlockStatus::OutOfMemory, "the directory could not grow: Reset the writer");
        }

        const std::size_t start = directory_.size();
        directory_.resize(start + entry_length);
        std::uint8_t* entry = directory_.data() + start;
        StoreLe(entry, id_length, 2);
        std::memcpy(entry + 2, id, id_length);
        StoreLe(entry + 2 + id_length, schema_id, 4);
        StoreLe(entry + 6 + id_length, static_cast<std::uint64_t>(row_count), 4);
        entry[10 + id_length] = 0;

        tables_.push_back(TableInfo{ start + 2, id_length, data_.size(), row_count, column_maps_.size(), 0 });
        column_count_offset_ = static_cast<std::int64_t>(start + entry_length - 1);
        row_count_ += row_count;
        return TableBlockStatus::Ok;
    }

    TableBlockStatus TableBlockWriter::AddColumn(TableColumnType type, const std::uint64_t* values, std::size_t count,
                                                 const std::uint8_t* nulls, std::size_t nulls_length) noexcept
    {
        return AddColumnBytes(type, values, count, nulls, nulls_length, false);
    }

    TableBlockStatus TableBlockWriter::AddDoubleColumn(const double* values, std::size_t count,
                                                       const std::uint8_t* nulls, std::size_t nulls_length) noexcept
    {
        return AddColumnBytes(TableColumnType::Double, values, count, nulls, nulls_length, true);
    }

    TableBlockStatus TableBlockWriter::AddColumnBytes(TableColumnType type, const void* values, std::size_t count,
                                                      const std::uint8_t* nulls, std::size_t nulls_length, bool doubles) noexcept
    {
        if (failed_)
            return Fail(TableBlockStatus::OutOfMemory, "the writer ran out of memory: Reset it first");
        if (column_count_offset_ < 0)
            return Fail(TableBlockStatus::WrongOrder, "call BeginTable before adding columns");
        if (!KnownType(type) || (doubles && type != TableColumnType::Double))
            return Fail(TableBlockStatus::InvalidArgument, "unknown column type");

        TableInfo& table = tables_.back();
        const bool first = table.columns == 0;
        if (first && type != TableColumnType::Time)
            return Fail(TableBlockStatus::WrongOrder, "the first column of a table is the time column");
        if (table.columns >= static_cast<std::size_t>(limits_.max_columns_per_table))
            return Fail(TableBlockStatus::LimitExceeded, "the table would have more columns than the limit");
        // table.rows is within 1..max_rows (an int32), so count * 8 below cannot overflow.
        if (count != static_cast<std::size_t>(table.rows))
            return Fail(TableBlockStatus::InvalidArgument, "the column does not have one value per row of the table");
        if (values == nullptr)
            return Fail(TableBlockStatus::InvalidArgument, "the column has no values");

        // The map: empty, or exactly ceil(rows / 8) bytes with zero padding bits; a map with no bit
        // set is no map.
        bool has_nulls = false;
        if (nulls != nullptr && nulls_length > 0)
        {
            if (nulls_length != MapLength(table.rows))
                return Fail(TableBlockStatus::InvalidArgument, "the null map does not have ceil(rows / 8) bytes");
            const unsigned tail = static_cast<unsigned>(table.rows & 7);
            if (tail != 0 && (nulls[nulls_length - 1] >> tail) != 0)
                return Fail(TableBlockStatus::InvalidArgument, "the null map has bits set past the last row");
            for (std::size_t i = 0; i < nulls_length && !has_nulls; ++i)
                has_nulls = nulls[i] != 0;
        }
        if (first && has_nulls)
            return Fail(TableBlockStatus::InvalidArgument, "the first column is the time of the row, which is never null");

        const auto* source = static_cast<const std::uint8_t*>(values);
        const std::size_t bytes = count * TableBlockFormat::kValueSize;

        std::uint64_t min_time = std::numeric_limits<std::uint64_t>::max();
        std::uint64_t max_time = 0;
        if (type == TableColumnType::Time)
        {
            for (std::size_t i = 0; i < count; ++i)
            {
                if (has_nulls && IsNull(nulls, i))
                    continue;
                // A negative int64 is above the maximum as uint64, so one comparison checks the range.
                const std::uint64_t t = LoadValue(source + i * TableBlockFormat::kValueSize);
                if (t > TableBlockFormat::kMaxTimeTicks)
                    return Fail(TableBlockStatus::InvalidArgument, "a time must be within 0..3155378975999999999 ticks");
                min_time = std::min(min_time, t);
                max_time = std::max(max_time, t);
            }
        }

        // The type byte and the values (the first column was counted by BeginTable), and the map.
        const std::uint64_t growth = (first ? 0 : 1 + static_cast<std::uint64_t>(bytes)) +
                                     (has_nulls ? static_cast<std::uint64_t>(nulls_length) : 0);
        const TableBlockStatus status = CheckGrowth(growth);
        if (status != TableBlockStatus::Ok)
            return status;

        // Every allocation first; nothing below can throw once they have succeeded, so a failure
        // leaves the writer's content as it was.
        try
        {
            Reserve(column_maps_, column_maps_.size() + 1);
            Reserve(directory_, directory_.size() + 1);
            Reserve(data_, data_.size() + bytes);
            if (has_nulls)
                Reserve(null_data_, null_data_.size() + nulls_length);
        }
        catch (...)
        {
            failed_ = true;
            return Fail(TableBlockStatus::OutOfMemory, "the column could not be stored: Reset the writer");
        }

        directory_.push_back(static_cast<std::uint8_t>(static_cast<std::uint8_t>(type) | (has_nulls ? TableBlockFormat::kNullFlag : 0)));
        const std::size_t at = data_.size();
        data_.resize(at + bytes);
        // Null cells are written as zero without reading the caller's value.
        CopyColumnLe(data_.data() + at, source, count, has_nulls ? nulls : nullptr, !kLittleEndian);

        if (has_nulls)
        {
            column_maps_.push_back(static_cast<std::int64_t>(null_data_.size()));
            null_data_.insert(null_data_.end(), nulls, nulls + nulls_length);
        }
        else
        {
            column_maps_.push_back(-1);
        }

        if (first)
        {
            if (tables_.size() == 1 || min_time < first_time_)
                first_time_ = min_time;
            if (tables_.size() == 1 || max_time > last_time_)
                last_time_ = max_time;
        }

        ++table.columns;
        directory_[static_cast<std::size_t>(column_count_offset_)] = static_cast<std::uint8_t>(table.columns);
        max_columns_ = std::max(max_columns_, table.columns);
        return TableBlockStatus::Ok;
    }

    bool TableBlockWriter::FindDuplicate()
    {
        // Sort the tables by identifier bytes and compare neighbours: O(n log n) comparisons whatever
        // the identifiers are.
        Reserve(order_, tables_.size());
        order_.resize(tables_.size());
        for (std::size_t i = 0; i < order_.size(); ++i)
            order_[i] = i;

        const auto compare = [this](std::size_t a, std::size_t b) {
            const TableInfo& x = tables_[a];
            const TableInfo& y = tables_[b];
            const std::uint8_t* px = directory_.data() + x.id_offset;
            const std::uint8_t* py = directory_.data() + y.id_offset;
            return std::lexicographical_compare(px, px + x.id_length, py, py + y.id_length);
        };
        std::sort(order_.begin(), order_.end(), compare);

        for (std::size_t i = 1; i < order_.size(); ++i)
            if (!compare(order_[i - 1], order_[i]))
                return true;
        return false;
    }

    void TableBlockWriter::LayOut(std::uint8_t* payload) const noexcept
    {
        std::memcpy(payload, directory_.data(), directory_.size());
        std::size_t at = directory_.size();

        // Columns, column-major: column c of every table that has one, pass after pass.
        for (std::size_t c = 0; c < max_columns_; ++c)
        {
            for (const TableInfo& table : tables_)
            {
                if (c >= table.columns)
                    continue;
                const std::size_t size = static_cast<std::size_t>(table.rows) * TableBlockFormat::kValueSize;
                std::memcpy(payload + at, data_.data() + table.data_offset + c * size, size);
                at += size;
            }
        }

        // The null maps in the same (pass, table) order, flagged columns only.
        if (null_data_.empty())
            return;
        for (std::size_t c = 1; c < max_columns_; ++c)
        {
            for (const TableInfo& table : tables_)
            {
                if (c >= table.columns)
                    continue;
                const std::int64_t offset = column_maps_[table.first_column + c];
                if (offset < 0)
                    continue;
                const std::size_t size = MapLength(table.rows);
                std::memcpy(payload + at, null_data_.data() + offset, size);
                at += size;
            }
        }
    }

    TableBlockStatus TableBlockWriter::Finish(std::vector<std::uint8_t>& out, std::size_t reserve_front, std::uint64_t max_frame_bytes) noexcept
    {
        out.clear();
        if (failed_)
            return Fail(TableBlockStatus::OutOfMemory, "the writer ran out of memory: Reset it first");
        if (tables_.empty())
            return Fail(TableBlockStatus::WrongOrder, "a block holds at least one table");
        if (tables_.back().columns == 0)
            return Fail(TableBlockStatus::WrongOrder, "the open table has no columns: add its time column first");
        // Checked before it is added to anything: the reservation is a frame header, not a buffer.
        if (reserve_front > kMaxReserveFront)
            return Fail(TableBlockStatus::InvalidArgument, "the front reservation is larger than kMaxReserveFront");

        // Finish changes none of the writer's tables: a refusal below leaves them for the caller.
        // The limit kept the payload within 1 GiB, so every length below fits the header's int32 and
        // reserve_front + 44 + ZSTD_compressBound(payload) fits a size_t.
        const std::size_t payload_length = static_cast<std::size_t>(DecompressedLength());
        const std::size_t header_at = reserve_front;
        const std::size_t capacity = reserve_front + TableBlockFormat::kHeaderSize + ZSTD_compressBound(payload_length);
        const char* error = nullptr;
        try
        {
            if (FindDuplicate())
                return Fail(TableBlockStatus::DuplicateTable, "two tables have the same identifier: a table appears once in a block");

            Reserve(payload_, payload_length);
            payload_.resize(payload_length);
            LayOut(payload_.data());

            out.reserve(capacity);
            out.assign(reserve_front + TableBlockFormat::kHeaderSize, 0);
            if (!compressor_.CompressFrame(payload_.data(), payload_length, out, error))
            {
                out.clear();
                return Fail(TableBlockStatus::CompressionFailed, error);
            }
        }
        catch (...)
        {
            out.clear();
            return Fail(TableBlockStatus::OutOfMemory, "the block could not be built: out of memory");
        }

        const std::size_t compressed = out.size() - header_at - TableBlockFormat::kHeaderSize;
        if (compressed > static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max()) ||
            (max_frame_bytes != 0 && out.size() > max_frame_bytes))
        {
            out.clear();
            return Fail(TableBlockStatus::FrameTooLarge, "the block does not fit the frame limit: split it");
        }

        std::uint8_t* header = out.data() + header_at;
        header[0] = 'H';
        header[1] = 'T';
        header[2] = 'B';
        header[3] = 'K';
        header[4] = TableBlockFormat::kVersion;
        // Bytes 5..7 are reserved and stay zero.
        StoreLe(header + 8, static_cast<std::uint64_t>(row_count_), 4);
        StoreLe(header + 12, tables_.size(), 4);
        StoreLe(header + 16, first_time_, 8);
        StoreLe(header + 24, last_time_, 8);
        StoreLe(header + 32, compressed, 4);
        StoreLe(header + 36, payload_length, 4);

        std::uint32_t crc = TableBlockCrc32(header, TableBlockFormat::kChecksumOffset);
        crc = TableBlockCrc32(header + TableBlockFormat::kHeaderSize, compressed, crc);
        StoreLe(header + TableBlockFormat::kChecksumOffset, crc, 4);
        return TableBlockStatus::Ok;
    }
} // namespace hsm::collector::tables
