#pragma once

/// @file
/// @brief Writer of a table block, format version 1 (#1848). Internal: compiled only with
/// HSM_COLLECTOR_TABLES, never part of the public headers.
///
/// The byte layout is this repository's format, specified in
/// aicontext/features/api/table-block/feature.md (the reference implementation is the managed
/// src/api/HSMTableBlocks). A block is a 44-byte header followed by one zstd frame; inside the frame a
/// directory, the columns as raw 8-byte little-endian values, column-major, then the null maps. The
/// compression is libzstd's and the checksum zlib's CRC-32; only the layout is written here.

#include "hsm_table_format.hpp"
#include "hsm_zstd.hpp"

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace hsm::collector::tables
{
    /// What a call of the writer did. Every status but Ok comes with LastError().
    enum class TableBlockStatus
    {
        Ok = 0,
        /// A call out of order: a column before BeginTable, a first column that is not the time, a
        /// table without columns, a block without tables, a call after OutOfMemory without Reset.
        WrongOrder,
        /// The input breaks a rule of the format: a wrong value or map length, map padding bits, a
        /// null time, a time out of range, an empty identifier, a table without rows.
        InvalidArgument,
        /// A limit would be exceeded (tables, rows, columns per table, identifier length, payload
        /// length). Nothing was written: send what the writer holds and start a new block.
        LimitExceeded,
        /// Two tables share an identifier.
        DuplicateTable,
        /// The compressed block does not fit the frame limit given to Finish: it must be split.
        FrameTooLarge,
        /// libzstd refused.
        CompressionFailed,
        /// Memory could not grow. The writer must be Reset before it is used again.
        OutOfMemory,
    };

    /// Builds one block from tables. Add a table with BeginTable, then its time column, then its other
    /// columns; Finish writes the block. The rows keep the order given: the writer neither sorts nor
    /// requires time order. Every check runs before anything is written, so a refused call leaves the
    /// writer as it was (except OutOfMemory, after which only Reset is accepted).
    ///
    /// Not thread-safe: one owner thread. Meant to be reused, one block per tick: Reset keeps the
    /// buffers and the zstd context. No call throws.
    class TableBlockWriter
    {
    public:
        explicit TableBlockWriter(const TableBlockLimits& limits = TableBlockLimits{}, int level = ZstdCompressor::kDefaultLevel);

        TableBlockWriter(const TableBlockWriter&) = delete;
        TableBlockWriter& operator=(const TableBlockWriter&) = delete;

        /// Opens a table of `row_count` rows and closes the previous one. The identifier is opaque bytes,
        /// unique within the block (Finish checks that).
        TableBlockStatus BeginTable(const std::uint8_t* id, std::size_t id_length, std::uint32_t schema_id, std::int64_t row_count);

        /// Adds a column of the open table: `count` (the table's row count) values as their 64-bit
        /// patterns (int64 two's complement, double IEEE 754 bits, time as ticks within
        /// 0..kMaxTimeTicks). `nulls` is null/empty (no nulls) or a map of ceil(rows / 8) bytes, row i
        /// at bit i % 8 of byte i / 8, padding bits zero; a map with no bit set is no map. The values of
        /// null rows are written as zero. The first column is the time and is never null.
        TableBlockStatus AddColumn(TableColumnType type, const std::uint64_t* values, std::size_t count,
                                   const std::uint8_t* nulls = nullptr, std::size_t nulls_length = 0);

        /// AddColumn of a double column, copied bit for bit.
        TableBlockStatus AddDoubleColumn(const double* values, std::size_t count,
                                         const std::uint8_t* nulls = nullptr, std::size_t nulls_length = 0);

        /// Closes the open table and writes the block into `out` (replacing its content) after
        /// `reserve_front` zero bytes the caller fills later (a transport's frame header). When
        /// `max_frame_bytes` is not 0, `reserve_front` plus the block must fit it, else FrameTooLarge and
        /// `out` is empty. The writer keeps its tables; call Reset for the next block.
        TableBlockStatus Finish(std::vector<std::uint8_t>& out, std::size_t reserve_front = 0, std::uint64_t max_frame_bytes = 0);

        /// Forgets every table, keeps the buffers and the zstd context.
        void Reset() noexcept;

        std::int32_t TableCount() const noexcept { return static_cast<std::int32_t>(tables_.size()); }
        std::int32_t RowCount() const noexcept { return static_cast<std::int32_t>(row_count_); }
        /// The exact payload length now: directory, every column and every null map added.
        std::int64_t DecompressedLength() const noexcept;
        const TableBlockLimits& Limits() const noexcept { return limits_; }
        /// What the last refused call said.
        const std::string& LastError() const noexcept { return error_; }

    private:
        struct TableInfo
        {
            std::size_t id_offset;
            std::size_t id_length;
            std::size_t data_offset;
            std::int64_t rows;
            std::size_t first_column;
            std::size_t columns;
        };

        TableBlockStatus Fail(TableBlockStatus status, std::string message);
        TableBlockStatus CheckGrowth(std::int64_t bytes);
        TableBlockStatus AddColumnBytes(TableColumnType type, const void* values, std::size_t count,
                                        const std::uint8_t* nulls, std::size_t nulls_length, bool doubles);
        bool FindDuplicate(std::size_t& first);
        void LayOut(std::uint8_t* payload) const;

        TableBlockLimits limits_;
        bool limits_valid_;
        ZstdCompressor compressor_;

        std::vector<std::uint8_t> directory_;
        // Columns as added, table after table; Finish lays them out column-major.
        std::vector<std::uint8_t> data_;
        // Null maps as added, and per column (every table's columns in turn) its map's offset or -1.
        std::vector<std::uint8_t> null_data_;
        std::vector<std::int64_t> column_maps_;
        std::vector<TableInfo> tables_;
        std::vector<std::uint8_t> payload_;
        std::vector<std::size_t> order_;

        std::int64_t row_count_ = 0;
        std::uint64_t first_time_ = 0;
        std::uint64_t last_time_ = 0;
        std::size_t max_columns_ = 0;
        // The open table: the offset of its column count byte in the directory, or -1.
        std::int64_t column_count_offset_ = -1;
        bool failed_ = false;
        std::string error_;
    };
} // namespace hsm::collector::tables
