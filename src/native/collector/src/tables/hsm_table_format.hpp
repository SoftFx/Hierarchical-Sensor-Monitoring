#pragma once

/// @file
/// @brief The constants and limits of the table block format, version 1 (#1848). Internal, never part of
/// the public headers.
///
/// Split from hsm_table_block_writer.hpp so that code which only checks a schema or a path (the table
/// sensors of a build without HSM_COLLECTOR_TABLES) needs neither libzstd nor zlib. The byte layout is
/// specified in aicontext/features/api/table-block/feature.md.

#include <cstddef>
#include <cstdint>

namespace hsm::collector::tables
{
    /// The column types of the format; the numbers are format constants.
    enum class TableColumnType : std::uint8_t
    {
        Time = 1,
        Int64 = 2,
        Double = 3,
    };

    /// Constants of format version 1.
    struct TableBlockFormat
    {
        static constexpr std::uint8_t kVersion = 1;
        static constexpr std::size_t kHeaderSize = 44;
        static constexpr std::size_t kValueSize = 8;
        static constexpr std::size_t kChecksumOffset = 40;
        /// DateTime.MaxValue.Ticks: the largest time value a block may carry.
        static constexpr std::uint64_t kMaxTimeTicks = 3155378975999999999ULL;
        /// The type byte's flag: this column of this table carries a null map in this block.
        static constexpr std::uint8_t kNullFlag = 0x80;
        static constexpr std::uint8_t kTypeMask = 0x7F;
        /// id length (2), schema id (4), row count (4), column count (1).
        static constexpr std::size_t kEntryFixedSize = 11;

        static constexpr std::int64_t kFormatMaxDecompressedBytes = std::int64_t{ 1 } << 30;
        static constexpr std::int32_t kFormatMaxColumnsPerTable = 255;
        static constexpr std::int32_t kFormatMaxTableIdLength = 65535;
    };

    /// The limits a block keeps to; the defaults are the managed TableBlockLimits.Default, so a block
    /// written with them reads with a reader's defaults.
    struct TableBlockLimits
    {
        std::int64_t max_decompressed_bytes = 64 * 1024 * 1024;
        std::int32_t max_tables = 65536;
        std::int32_t max_rows = 8 * 1024 * 1024;
        std::int32_t max_columns_per_table = 64;
        std::int32_t max_table_id_length = 1024;

        /// True when every value is at least 1 and within the format's maximum.
        bool Valid() const noexcept
        {
            return max_decompressed_bytes >= 1 && max_decompressed_bytes <= TableBlockFormat::kFormatMaxDecompressedBytes &&
                   max_tables >= 1 && max_rows >= 1 &&
                   max_columns_per_table >= 1 && max_columns_per_table <= TableBlockFormat::kFormatMaxColumnsPerTable &&
                   max_table_id_length >= 1 && max_table_id_length <= TableBlockFormat::kFormatMaxTableIdLength;
        }

        bool operator==(const TableBlockLimits& other) const noexcept
        {
            return max_decompressed_bytes == other.max_decompressed_bytes && max_tables == other.max_tables &&
                   max_rows == other.max_rows && max_columns_per_table == other.max_columns_per_table &&
                   max_table_id_length == other.max_table_id_length;
        }

        bool operator!=(const TableBlockLimits& other) const noexcept { return !(*this == other); }
    };

    /// CRC-32 of the format (IEEE 802.3, zlib's crc32_z): `crc` is the value of the bytes before.
    /// Defined only with HSM_COLLECTOR_TABLES (it calls zlib).
    std::uint32_t TableBlockCrc32(const std::uint8_t* data, std::size_t size, std::uint32_t crc = 0) noexcept;
} // namespace hsm::collector::tables
