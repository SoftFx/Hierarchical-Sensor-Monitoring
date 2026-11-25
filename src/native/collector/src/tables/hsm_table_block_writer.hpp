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

#include "hsm_zstd.hpp"

#include <cstddef>
#include <cstdint>
#include <vector>

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
        bool Valid() const noexcept;
    };

    /// What a call of the writer did. Every status but Ok comes with LastError(), a static text.
    enum class TableBlockStatus
    {
        Ok = 0,
        /// A call out of order: a column before BeginTable, a first column that is not the time, a
        /// table without columns, a block without tables.
        WrongOrder,
        /// The input breaks a rule of the format or of the call: a wrong value or map length, map
        /// padding bits, a null time, a time out of range, an empty identifier, a row count below 1,
        /// a front reservation above kMaxReserveFront.
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
        /// Memory could not grow. From BeginTable or AddColumn: the writer is failed and refuses every
        /// call with OutOfMemory until Reset. From Finish: only that block could not be built.
        OutOfMemory,
    };

    /// CRC-32 of the format (IEEE 802.3, zlib's crc32_z): `crc` is the value of the bytes before.
    std::uint32_t TableBlockCrc32(const std::uint8_t* data, std::size_t size, std::uint32_t crc = 0) noexcept;

    /// Copies `count` 8-byte values from `source` (host byte order) to `destination` in little-endian:
    /// as they are when `swap` is false (a little-endian host), each value's bytes reversed when it is
    /// true (a big-endian host). A row marked in `nulls` (null: none) is written as eight zero bytes and
    /// its source is not read. The writer passes swap = !little-endian host; the function takes it as an
    /// argument so that the big-endian path is tested on every host.
    void CopyColumnLe(std::uint8_t* destination, const std::uint8_t* source, std::size_t count,
                      const std::uint8_t* nulls, bool swap) noexcept;

    /// Builds one block from tables. Add a table with BeginTable, then its time column, then its other
    /// columns; Finish writes the block. The rows keep the order given: the writer neither sorts nor
    /// requires time order.
    ///
    /// Refusals. Every argument and limit is checked, in overflow-safe arithmetic, before anything is
    /// written, so a refused BeginTable or AddColumn leaves the writer exactly as it was. The one
    /// exception is OutOfMemory from BeginTable or AddColumn: the writer is then failed (its content
    /// stays consistent, nothing is half added) and refuses every call until Reset.
    ///
    /// Finish never changes the writer, whatever it returns: the tables stay, the last table stays open
    /// (more columns may still be added to it) and more tables may follow; a later Finish writes them
    /// all. So after FrameTooLarge (or LimitExceeded from an add) the caller still holds every table it
    /// added, and splits by Reset and adding a part of its rows again: the pipeline packs the parts from
    /// its own rows, and DecompressedLength() lets it cut before a limit is reached.
    ///
    /// Not thread-safe: one owner thread. Meant to be reused, one block per tick: Reset keeps the
    /// buffers and the zstd context. No call throws, and no failure path allocates.
    class TableBlockWriter
    {
    public:
        /// The largest front reservation Finish accepts (room for a transport frame header).
        static constexpr std::size_t kMaxReserveFront = 64 * 1024;

        explicit TableBlockWriter(const TableBlockLimits& limits = TableBlockLimits{}, int level = ZstdCompressor::kDefaultLevel);

        TableBlockWriter(const TableBlockWriter&) = delete;
        TableBlockWriter& operator=(const TableBlockWriter&) = delete;

        /// Opens a table of `row_count` rows. The identifier is opaque bytes, unique within the block
        /// (Finish checks that).
        TableBlockStatus BeginTable(const std::uint8_t* id, std::size_t id_length, std::uint32_t schema_id, std::int64_t row_count) noexcept;

        /// Adds a column of the open table: `count` (the table's row count) values as their 64-bit
        /// patterns (int64 two's complement, double IEEE 754 bits, time as ticks within
        /// 0..kMaxTimeTicks). `nulls` is null/empty (no nulls) or a map of ceil(rows / 8) bytes, row i
        /// at bit i % 8 of byte i / 8, padding bits zero; a map with no bit set is no map. The values of
        /// null rows are written as zero and not read. The first column is the time and is never null.
        TableBlockStatus AddColumn(TableColumnType type, const std::uint64_t* values, std::size_t count,
                                   const std::uint8_t* nulls = nullptr, std::size_t nulls_length = 0) noexcept;

        /// AddColumn of a double column, copied bit for bit.
        TableBlockStatus AddDoubleColumn(const double* values, std::size_t count,
                                         const std::uint8_t* nulls = nullptr, std::size_t nulls_length = 0) noexcept;

        /// Writes the block of every table added into `out` (replacing its content) after
        /// `reserve_front` zero bytes (at most kMaxReserveFront) that the caller fills later, a transport
        /// frame header. When `max_frame_bytes` is not 0, `reserve_front` plus the block must fit it,
        /// else FrameTooLarge. On any refusal `out` is empty. The writer is not changed (see the class).
        TableBlockStatus Finish(std::vector<std::uint8_t>& out, std::size_t reserve_front = 0, std::uint64_t max_frame_bytes = 0) noexcept;

        /// Forgets every table and clears a failure; keeps the buffers and the zstd context.
        void Reset() noexcept;

        std::int32_t TableCount() const noexcept { return static_cast<std::int32_t>(tables_.size()); }
        std::int32_t RowCount() const noexcept { return static_cast<std::int32_t>(row_count_); }
        /// The exact payload length now: directory, every column and every null map added.
        std::int64_t DecompressedLength() const noexcept;
        const TableBlockLimits& Limits() const noexcept { return limits_; }
        /// What the last refused call said: a static text, empty while nothing was refused.
        const char* LastError() const noexcept { return error_; }

        /// Test seam: how many times a buffer of the writer has been reallocated (never reset).
        std::size_t ReallocationsForTests() const noexcept { return reallocations_; }
        /// Test seam: the buffer growth after `successes` more succeed throws std::bad_alloc (-1: never).
        void FailAllocationAfterForTests(std::int64_t successes) noexcept { fail_allocation_after_ = successes; }

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

        TableBlockStatus Fail(TableBlockStatus status, const char* message) noexcept;
        TableBlockStatus CheckGrowth(std::uint64_t bytes) noexcept;
        TableBlockStatus AddColumnBytes(TableColumnType type, const void* values, std::size_t count,
                                        const std::uint8_t* nulls, std::size_t nulls_length, bool doubles) noexcept;
        // Makes room for `needed` elements, growing geometrically. May throw (bad_alloc, length_error).
        template <typename T>
        void Reserve(std::vector<T>& buffer, std::size_t needed);
        bool FindDuplicate();
        void LayOut(std::uint8_t* payload) const noexcept;

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
        const char* error_ = "";
        std::size_t reallocations_ = 0;
        std::int64_t fail_allocation_after_ = -1;
    };
} // namespace hsm::collector::tables
