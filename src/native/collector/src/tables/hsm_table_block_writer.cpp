#include "hsm_table_block_writer.hpp"

#include <zlib.h>

#include <algorithm>
#include <cstring>
#include <limits>
#include <new>
#include <utility>

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

    bool TableBlockLimits::Valid() const noexcept
    {
        return max_decompressed_bytes >= 1 && max_decompressed_bytes <= TableBlockFormat::kFormatMaxDecompressedBytes &&
               max_tables >= 1 && max_rows >= 1 &&
               max_columns_per_table >= 1 && max_columns_per_table <= TableBlockFormat::kFormatMaxColumnsPerTable &&
               max_table_id_length >= 1 && max_table_id_length <= TableBlockFormat::kFormatMaxTableIdLength;
    }

    std::uint32_t TableBlockCrc32(const std::uint8_t* data, std::size_t size, std::uint32_t crc) noexcept
    {
        return static_cast<std::uint32_t>(crc32_z(crc, data, size));
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
        error_.clear();
    }

    TableBlockStatus TableBlockWriter::Fail(TableBlockStatus status, std::string message)
    {
        if (status == TableBlockStatus::OutOfMemory)
            failed_ = true;
        try
        {
            error_ = std::move(message);
        }
        catch (...)
        {
            // The status still says what happened.
        }
        return status;
    }

    TableBlockStatus TableBlockWriter::CheckGrowth(std::int64_t bytes)
    {
        if (DecompressedLength() + bytes > limits_.max_decompressed_bytes)
            return Fail(TableBlockStatus::LimitExceeded,
                        "the payload would exceed " + std::to_string(limits_.max_decompressed_bytes) +
                            " bytes: send the block and start a new one");
        return TableBlockStatus::Ok;
    }

    TableBlockStatus TableBlockWriter::BeginTable(const std::uint8_t* id, std::size_t id_length, std::uint32_t schema_id, std::int64_t row_count)
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
            return Fail(TableBlockStatus::LimitExceeded,
                        "the identifier is " + std::to_string(id_length) + " bytes, at most " +
                            std::to_string(limits_.max_table_id_length) + " are allowed");
        if (row_count < 1)
            return Fail(TableBlockStatus::InvalidArgument, "a table has at least one row");
        if (tables_.size() + 1 > static_cast<std::size_t>(limits_.max_tables))
            return Fail(TableBlockStatus::LimitExceeded, "the block would hold more than " + std::to_string(limits_.max_tables) + " tables");
        // max_rows is an int32, so this also keeps every row count inside the format's int32.
        if (row_count_ + row_count > limits_.max_rows)
            return Fail(TableBlockStatus::LimitExceeded, "the block would hold more than " + std::to_string(limits_.max_rows) + " rows");

        const std::size_t entry_length = TableBlockFormat::kEntryFixedSize + id_length;
        // The entry, its first column's type byte and the first column's values.
        const TableBlockStatus growth = CheckGrowth(static_cast<std::int64_t>(entry_length) + 1 +
                                                    row_count * static_cast<std::int64_t>(TableBlockFormat::kValueSize));
        if (growth != TableBlockStatus::Ok)
            return growth;

        try
        {
            const std::size_t start = directory_.size();
            tables_.reserve(tables_.size() + 1);
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
        }
        catch (const std::bad_alloc&)
        {
            return Fail(TableBlockStatus::OutOfMemory, "the directory could not grow");
        }

        return TableBlockStatus::Ok;
    }

    TableBlockStatus TableBlockWriter::AddColumn(TableColumnType type, const std::uint64_t* values, std::size_t count,
                                                 const std::uint8_t* nulls, std::size_t nulls_length)
    {
        return AddColumnBytes(type, values, count, nulls, nulls_length, false);
    }

    TableBlockStatus TableBlockWriter::AddDoubleColumn(const double* values, std::size_t count,
                                                       const std::uint8_t* nulls, std::size_t nulls_length)
    {
        return AddColumnBytes(TableColumnType::Double, values, count, nulls, nulls_length, true);
    }

    TableBlockStatus TableBlockWriter::AddColumnBytes(TableColumnType type, const void* values, std::size_t count,
                                                      const std::uint8_t* nulls, std::size_t nulls_length, bool doubles)
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
        if (table.columns + 1 > static_cast<std::size_t>(limits_.max_columns_per_table))
            return Fail(TableBlockStatus::LimitExceeded,
                        "a table would have more than " + std::to_string(limits_.max_columns_per_table) + " columns");
        if (count != static_cast<std::size_t>(table.rows))
            return Fail(TableBlockStatus::InvalidArgument,
                        "the column has " + std::to_string(count) + " values, the table " + std::to_string(table.rows) + " rows");
        if (values == nullptr)
            return Fail(TableBlockStatus::InvalidArgument, "the column has no values");

        // The map: empty, or exactly ceil(rows / 8) bytes with zero padding bits; a map with no bit
        // set is no map.
        bool has_nulls = false;
        if (nulls != nullptr && nulls_length > 0)
        {
            const std::size_t needed = MapLength(table.rows);
            if (nulls_length != needed)
                return Fail(TableBlockStatus::InvalidArgument,
                            "the null map has " + std::to_string(nulls_length) + " bytes, a column of " +
                                std::to_string(table.rows) + " rows needs " + std::to_string(needed));
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
        const std::int64_t growth = (first ? 0 : 1 + static_cast<std::int64_t>(bytes)) +
                                    (has_nulls ? static_cast<std::int64_t>(nulls_length) : 0);
        if (growth > 0)
        {
            const TableBlockStatus status = CheckGrowth(growth);
            if (status != TableBlockStatus::Ok)
                return status;
        }

        try
        {
            column_maps_.reserve(column_maps_.size() + 1);
            if (has_nulls)
                null_data_.reserve(null_data_.size() + nulls_length);
            directory_.push_back(static_cast<std::uint8_t>(static_cast<std::uint8_t>(type) | (has_nulls ? TableBlockFormat::kNullFlag : 0)));
            const std::size_t at = data_.size();
            data_.resize(at + bytes);

            std::uint8_t* destination = data_.data() + at;
            if (kLittleEndian)
            {
                std::memcpy(destination, source, bytes);
            }
            else
            {
                for (std::size_t i = 0; i < count; ++i)
                    StoreLe(destination + i * TableBlockFormat::kValueSize, LoadValue(source + i * TableBlockFormat::kValueSize), TableBlockFormat::kValueSize);
            }

            if (has_nulls)
            {
                // A null cell holds eight zero bytes, whatever the caller left in it.
                for (std::size_t row = 0; row < count; ++row)
                    if (IsNull(nulls, row))
                        std::memset(destination + row * TableBlockFormat::kValueSize, 0, TableBlockFormat::kValueSize);

                column_maps_.push_back(static_cast<std::int64_t>(null_data_.size()));
                null_data_.insert(null_data_.end(), nulls, nulls + nulls_length);
            }
            else
            {
                column_maps_.push_back(-1);
            }
        }
        catch (const std::bad_alloc&)
        {
            return Fail(TableBlockStatus::OutOfMemory, "the column could not be stored");
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

    bool TableBlockWriter::FindDuplicate(std::size_t& first)
    {
        // Sort the tables by identifier bytes and compare neighbours: O(n log n) comparisons whatever
        // the identifiers are.
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
        {
            if (!compare(order_[i - 1], order_[i]))
            {
                first = std::min(order_[i - 1], order_[i]);
                return true;
            }
        }
        return false;
    }

    void TableBlockWriter::LayOut(std::uint8_t* payload) const
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

    TableBlockStatus TableBlockWriter::Finish(std::vector<std::uint8_t>& out, std::size_t reserve_front, std::uint64_t max_frame_bytes)
    {
        out.clear();
        if (failed_)
            return Fail(TableBlockStatus::OutOfMemory, "the writer ran out of memory: Reset it first");
        if (tables_.empty())
            return Fail(TableBlockStatus::WrongOrder, "a block holds at least one table");
        if (tables_.back().columns == 0)
            return Fail(TableBlockStatus::WrongOrder, "the open table has no columns: add its time column first");

        try
        {
            std::size_t duplicate = 0;
            if (FindDuplicate(duplicate))
                return Fail(TableBlockStatus::DuplicateTable,
                            "table " + std::to_string(duplicate) + " has the identifier of another table: a table appears once in a block");
            column_count_offset_ = -1;

            // The limit kept this within 1 GiB, so every length below fits the header's int32.
            const std::size_t payload_length = static_cast<std::size_t>(DecompressedLength());
            payload_.resize(payload_length);
            LayOut(payload_.data());

            const std::size_t header_at = reserve_front;
            out.assign(reserve_front + TableBlockFormat::kHeaderSize, 0);
            std::string error;
            if (!compressor_.CompressFrame(payload_.data(), payload_length, out, error))
            {
                out.clear();
                return Fail(TableBlockStatus::CompressionFailed, std::move(error));
            }

            const std::size_t compressed = out.size() - header_at - TableBlockFormat::kHeaderSize;
            if (compressed > static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max()) ||
                (max_frame_bytes != 0 && out.size() > max_frame_bytes))
            {
                const std::size_t size = out.size();
                out.clear();
                return Fail(TableBlockStatus::FrameTooLarge,
                            "the block takes " + std::to_string(size) + " bytes with its frame header, the frame limit is " +
                                std::to_string(max_frame_bytes) + ": split it");
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
        }
        catch (const std::bad_alloc&)
        {
            out.clear();
            return Fail(TableBlockStatus::OutOfMemory, "the block could not be built");
        }

        return TableBlockStatus::Ok;
    }
} // namespace hsm::collector::tables
