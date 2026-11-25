#pragma once

// Test-only reader of a table block, format version 1 (#1848). It is never installed or shipped: the
// collector writes blocks and never reads one. It verifies a block as strictly as the managed
// TableBlockReader (aicontext/features/api/table-block/feature.md, "Errors"): lengths, checksum, header
// fields, limits, exactly one zstd frame, the directory, the null maps and the times. The checks are
// made in the order the specification lists them, and a refusal carries the managed error code's name.
// Decompression is libzstd's and the checksum zlib's.

#include "../src/tables/hsm_table_block_writer.hpp"

#include <zstd.h>
#include <zstd_errors.h>

#include <algorithm>
#include <cstdint>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace hsm::collector::tables::test
{
    /// The managed TableBlockError codes.
    enum class TableBlockError
    {
        BadMagic,
        UnknownVersion,
        Truncated,
        LengthMismatch,
        ChecksumMismatch,
        LimitExceeded,
        CorruptHeader,
        CorruptFrame,
        CorruptDirectory,
        DirectoryOutOfBounds,
        DuplicateTable,
        InvalidTime,
        HeaderMismatch,
        CorruptNullMap,
    };

    inline const char* ErrorName(TableBlockError error)
    {
        switch (error)
        {
        case TableBlockError::BadMagic:
            return "BadMagic";
        case TableBlockError::UnknownVersion:
            return "UnknownVersion";
        case TableBlockError::Truncated:
            return "Truncated";
        case TableBlockError::LengthMismatch:
            return "LengthMismatch";
        case TableBlockError::ChecksumMismatch:
            return "ChecksumMismatch";
        case TableBlockError::LimitExceeded:
            return "LimitExceeded";
        case TableBlockError::CorruptHeader:
            return "CorruptHeader";
        case TableBlockError::CorruptFrame:
            return "CorruptFrame";
        case TableBlockError::CorruptDirectory:
            return "CorruptDirectory";
        case TableBlockError::DirectoryOutOfBounds:
            return "DirectoryOutOfBounds";
        case TableBlockError::DuplicateTable:
            return "DuplicateTable";
        case TableBlockError::InvalidTime:
            return "InvalidTime";
        case TableBlockError::HeaderMismatch:
            return "HeaderMismatch";
        case TableBlockError::CorruptNullMap:
            return "CorruptNullMap";
        }
        return "?";
    }

    class TableBlockReadError : public std::runtime_error
    {
    public:
        TableBlockReadError(TableBlockError error, const std::string& message)
            : std::runtime_error(std::string(ErrorName(error)) + ": " + message), error_(error)
        {
        }

        TableBlockError Error() const noexcept { return error_; }

    private:
        TableBlockError error_;
    };

    struct ReadColumn
    {
        TableColumnType type = TableColumnType::Time;
        /// The 64-bit patterns, little-endian decoded; a null row holds 0.
        std::vector<std::uint64_t> values;
        /// The column's map as the block carries it; empty when the column has no null.
        std::vector<std::uint8_t> null_map;

        bool IsNull(std::size_t row) const { return !null_map.empty() && (null_map[row >> 3] & (1u << (row & 7))) != 0; }
    };

    struct ReadTable
    {
        std::vector<std::uint8_t> id;
        std::uint32_t schema_id = 0;
        std::int32_t rows = 0;
        std::vector<ReadColumn> columns;
    };

    struct ReadBlock
    {
        std::int32_t row_count = 0;
        std::int32_t table_count = 0;
        std::uint64_t first_time = 0;
        std::uint64_t last_time = 0;
        std::int32_t compressed_length = 0;
        std::int32_t decompressed_length = 0;
        std::vector<std::uint8_t> payload;
        std::vector<ReadTable> tables;
    };

    namespace detail
    {
        inline std::uint64_t LoadLe(const std::uint8_t* at, std::size_t width)
        {
            std::uint64_t value = 0;
            for (std::size_t i = 0; i < width; ++i)
                value |= static_cast<std::uint64_t>(at[i]) << (8 * i);
            return value;
        }

        inline std::int32_t LoadInt32(const std::uint8_t* at)
        {
            return static_cast<std::int32_t>(static_cast<std::uint32_t>(LoadLe(at, 4)));
        }

        [[noreturn]] inline void Refuse(TableBlockError error, const std::string& message)
        {
            throw TableBlockReadError(error, message);
        }

        struct Entry
        {
            std::size_t id_offset;
            std::size_t id_length;
            std::uint32_t schema_id;
            std::int32_t rows;
            std::vector<std::uint8_t> types;
        };
    } // namespace detail

    /// Verifies and decodes a whole block; throws TableBlockReadError for anything wrong.
    inline ReadBlock ReadTableBlock(const std::uint8_t* block, std::size_t size, const TableBlockLimits& limits = TableBlockLimits{})
    {
        using detail::LoadInt32;
        using detail::LoadLe;
        using detail::Refuse;
        constexpr std::size_t kHeader = TableBlockFormat::kHeaderSize;

        if (size < 5)
            Refuse(TableBlockError::Truncated, "shorter than the magic and the version");
        if (block[0] != 'H' || block[1] != 'T' || block[2] != 'B' || block[3] != 'K')
            Refuse(TableBlockError::BadMagic, "not HTBK");
        if (block[4] != TableBlockFormat::kVersion)
            Refuse(TableBlockError::UnknownVersion, "version " + std::to_string(block[4]));
        if (size < kHeader)
            Refuse(TableBlockError::Truncated, "shorter than the header");

        ReadBlock read;
        read.row_count = LoadInt32(block + 8);
        read.table_count = LoadInt32(block + 12);
        read.first_time = LoadLe(block + 16, 8);
        read.last_time = LoadLe(block + 24, 8);
        read.compressed_length = LoadInt32(block + 32);
        read.decompressed_length = LoadInt32(block + 36);

        if (read.compressed_length < 1)
            Refuse(TableBlockError::CorruptHeader, "compressed length not positive");
        const std::size_t compressed = static_cast<std::size_t>(read.compressed_length);
        if (size - kHeader < compressed)
            Refuse(TableBlockError::Truncated, "shorter than the declared compressed length");
        if (size - kHeader > compressed)
            Refuse(TableBlockError::LengthMismatch, "bytes after the frame");

        std::uint32_t crc = TableBlockCrc32(block, TableBlockFormat::kChecksumOffset);
        crc = TableBlockCrc32(block + kHeader, compressed, crc);
        if (crc != static_cast<std::uint32_t>(LoadLe(block + 40, 4)))
            Refuse(TableBlockError::ChecksumMismatch, "CRC-32 differs");

        if (block[5] != 0 || block[6] != 0 || block[7] != 0)
            Refuse(TableBlockError::CorruptHeader, "reserved bytes not zero");
        if (read.row_count < 1 || read.table_count < 1 || read.decompressed_length < 1 || read.row_count < read.table_count)
            Refuse(TableBlockError::CorruptHeader, "a length or count not positive, or fewer rows than tables");
        if (read.first_time > TableBlockFormat::kMaxTimeTicks || read.last_time > TableBlockFormat::kMaxTimeTicks || read.first_time > read.last_time)
            Refuse(TableBlockError::CorruptHeader, "first/last time out of range or out of order");

        if (read.decompressed_length > limits.max_decompressed_bytes || read.table_count > limits.max_tables || read.row_count > limits.max_rows)
            Refuse(TableBlockError::LimitExceeded, "a header count above the limits");
        const std::int64_t least = static_cast<std::int64_t>(read.table_count) * 13 + static_cast<std::int64_t>(read.row_count) * 8;
        if (least > read.decompressed_length)
            Refuse(TableBlockError::LengthMismatch, "the counts need more bytes than declared");

        // Exactly one zstd frame, no skippable frame, nothing after it.
        const std::uint8_t* frame = block + kHeader;
        if (compressed < 4 || LoadLe(frame, 4) != 0xFD2FB528u)
            Refuse(TableBlockError::CorruptFrame, "no zstd frame magic");
        const std::size_t frame_size = ZSTD_findFrameCompressedSize(frame, compressed);
        if (ZSTD_isError(frame_size) || frame_size != compressed)
            Refuse(TableBlockError::CorruptFrame, "not exactly one zstd frame");
        const unsigned long long content = ZSTD_getFrameContentSize(frame, compressed);
        if (content == ZSTD_CONTENTSIZE_ERROR)
            Refuse(TableBlockError::CorruptFrame, "unreadable frame header");
        if (content != ZSTD_CONTENTSIZE_UNKNOWN && content != static_cast<unsigned long long>(read.decompressed_length))
            Refuse(TableBlockError::LengthMismatch, "the frame's content size differs from the declared length");

        read.payload.resize(static_cast<std::size_t>(read.decompressed_length));
        const std::size_t produced = ZSTD_decompress(read.payload.data(), read.payload.size(), frame, compressed);
        if (ZSTD_isError(produced))
        {
            if (ZSTD_getErrorCode(produced) == ZSTD_error_dstSize_tooSmall)
                Refuse(TableBlockError::LengthMismatch, "the frame decompresses to more than the declared length");
            Refuse(TableBlockError::CorruptFrame, std::string("the frame does not decompress: ") + ZSTD_getErrorName(produced));
        }
        if (produced != read.payload.size())
            Refuse(TableBlockError::LengthMismatch, "the frame decompresses to less than the declared length");

        // The directory.
        const std::uint8_t* payload = read.payload.data();
        const std::size_t length = read.payload.size();
        std::size_t at = 0;
        std::vector<detail::Entry> entries;
        std::int64_t total_rows = 0;
        for (std::int32_t t = 0; t < read.table_count; ++t)
        {
            if (length - at < 2)
                Refuse(TableBlockError::DirectoryOutOfBounds, "the directory runs past the payload");
            const std::size_t id_length = static_cast<std::size_t>(LoadLe(payload + at, 2));
            if (id_length == 0)
                Refuse(TableBlockError::CorruptDirectory, "an empty identifier");
            if (id_length > static_cast<std::size_t>(limits.max_table_id_length))
                Refuse(TableBlockError::LimitExceeded, "an identifier above the limit");
            if (length - at - 2 < id_length + 9)
                Refuse(TableBlockError::DirectoryOutOfBounds, "the directory runs past the payload");
            detail::Entry entry;
            entry.id_offset = at + 2;
            entry.id_length = id_length;
            entry.schema_id = static_cast<std::uint32_t>(LoadLe(payload + at + 2 + id_length, 4));
            entry.rows = LoadInt32(payload + at + 6 + id_length);
            const std::size_t columns = payload[at + 10 + id_length];
            at += 11 + id_length;
            if (entry.rows < 1)
                Refuse(TableBlockError::CorruptDirectory, "a table without rows");
            if (columns < 1)
                Refuse(TableBlockError::CorruptDirectory, "a table without columns");
            if (columns > static_cast<std::size_t>(limits.max_columns_per_table))
                Refuse(TableBlockError::LimitExceeded, "more columns than the limit");
            if (length - at < columns)
                Refuse(TableBlockError::DirectoryOutOfBounds, "the directory runs past the payload");
            entry.types.assign(payload + at, payload + at + columns);
            at += columns;
            for (std::size_t c = 0; c < columns; ++c)
            {
                const std::uint8_t type = entry.types[c] & TableBlockFormat::kTypeMask;
                if (type < 1 || type > 3)
                    Refuse(TableBlockError::CorruptDirectory, "an unknown column type");
            }
            if ((entry.types[0] & TableBlockFormat::kTypeMask) != static_cast<std::uint8_t>(TableColumnType::Time))
                Refuse(TableBlockError::CorruptDirectory, "a first column that is not time");
            if ((entry.types[0] & TableBlockFormat::kNullFlag) != 0)
                Refuse(TableBlockError::CorruptNullMap, "a null flag on the time column");
            total_rows += entry.rows;
            entries.push_back(std::move(entry));
        }

        // Columns and maps, sized in 64 bits.
        std::size_t max_columns = 0;
        std::uint64_t column_bytes = 0;
        std::uint64_t map_bytes = 0;
        for (const detail::Entry& entry : entries)
        {
            max_columns = std::max(max_columns, entry.types.size());
            column_bytes += static_cast<std::uint64_t>(entry.rows) * 8 * entry.types.size();
            for (std::uint8_t type : entry.types)
                if ((type & TableBlockFormat::kNullFlag) != 0)
                    map_bytes += (static_cast<std::uint64_t>(entry.rows) + 7) / 8;
        }
        const std::uint64_t remaining = length - at;
        if (column_bytes + map_bytes > remaining)
            Refuse(TableBlockError::DirectoryOutOfBounds, "the columns and null maps run past the payload");
        if (column_bytes + map_bytes < remaining)
            Refuse(TableBlockError::LengthMismatch, "bytes after the last column or null map");

        // Identifiers unique: sort and compare neighbours.
        std::vector<std::size_t> order(entries.size());
        for (std::size_t i = 0; i < order.size(); ++i)
            order[i] = i;
        const auto less = [&](std::size_t a, std::size_t b) {
            const auto* pa = payload + entries[a].id_offset;
            const auto* pb = payload + entries[b].id_offset;
            return std::lexicographical_compare(pa, pa + entries[a].id_length, pb, pb + entries[b].id_length);
        };
        std::sort(order.begin(), order.end(), less);
        for (std::size_t i = 1; i < order.size(); ++i)
            if (!less(order[i - 1], order[i]))
                Refuse(TableBlockError::DuplicateTable, "two entries with the same identifier");

        if (total_rows != read.row_count)
            Refuse(TableBlockError::HeaderMismatch, "the directory's row total differs from the header");

        read.tables.resize(entries.size());
        for (std::size_t t = 0; t < entries.size(); ++t)
        {
            ReadTable& table = read.tables[t];
            table.id.assign(payload + entries[t].id_offset, payload + entries[t].id_offset + entries[t].id_length);
            table.schema_id = entries[t].schema_id;
            table.rows = entries[t].rows;
            table.columns.resize(entries[t].types.size());
            for (std::size_t c = 0; c < entries[t].types.size(); ++c)
                table.columns[c].type = static_cast<TableColumnType>(entries[t].types[c] & TableBlockFormat::kTypeMask);
        }

        for (std::size_t c = 0; c < max_columns; ++c)
        {
            for (std::size_t t = 0; t < entries.size(); ++t)
            {
                if (c >= entries[t].types.size())
                    continue;
                std::vector<std::uint64_t>& values = read.tables[t].columns[c].values;
                values.resize(static_cast<std::size_t>(entries[t].rows));
                for (std::uint64_t& value : values)
                {
                    value = LoadLe(payload + at, 8);
                    at += 8;
                }
            }
        }

        for (std::size_t c = 1; c < max_columns; ++c)
        {
            for (std::size_t t = 0; t < entries.size(); ++t)
            {
                if (c >= entries[t].types.size() || (entries[t].types[c] & TableBlockFormat::kNullFlag) == 0)
                    continue;
                const std::size_t rows = static_cast<std::size_t>(entries[t].rows);
                const std::size_t map_length = (rows + 7) / 8;
                ReadColumn& column = read.tables[t].columns[c];
                column.null_map.assign(payload + at, payload + at + map_length);
                at += map_length;

                const std::string where = "table " + std::to_string(t) + ", column " + std::to_string(c);
                if ((rows & 7) != 0 && (column.null_map.back() >> (rows & 7)) != 0)
                    Refuse(TableBlockError::CorruptNullMap, "padding bits set in the map of " + where);
                if (std::all_of(column.null_map.begin(), column.null_map.end(), [](std::uint8_t b) { return b == 0; }))
                    Refuse(TableBlockError::CorruptNullMap, "a map with no null row in " + where);
                for (std::size_t r = 0; r < rows; ++r)
                    if (column.IsNull(r) && column.values[r] != 0)
                        Refuse(TableBlockError::CorruptNullMap, "a non-zero null cell in " + where + ", row " + std::to_string(r));
            }
        }

        std::uint64_t first = TableBlockFormat::kMaxTimeTicks;
        std::uint64_t last = 0;
        for (const ReadTable& table : read.tables)
        {
            for (std::size_t c = 0; c < table.columns.size(); ++c)
            {
                const ReadColumn& column = table.columns[c];
                if (column.type != TableColumnType::Time)
                    continue;
                for (std::size_t r = 0; r < column.values.size(); ++r)
                {
                    if (column.IsNull(r))
                        continue;
                    if (column.values[r] > TableBlockFormat::kMaxTimeTicks)
                        Refuse(TableBlockError::InvalidTime, "a time outside 0..DateTime.MaxValue.Ticks");
                    if (c == 0)
                    {
                        first = std::min(first, column.values[r]);
                        last = std::max(last, column.values[r]);
                    }
                }
            }
        }
        if (first != read.first_time || last != read.last_time)
            Refuse(TableBlockError::HeaderMismatch, "the first or last time differs from the header");

        return read;
    }

    inline ReadBlock ReadTableBlock(const std::vector<std::uint8_t>& block, const TableBlockLimits& limits = TableBlockLimits{})
    {
        return ReadTableBlock(block.data(), block.size(), limits);
    }
} // namespace hsm::collector::tables::test
