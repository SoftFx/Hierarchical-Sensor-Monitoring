#pragma once

/// @file
/// @brief The schema of a table sensor and its id (#1848). Internal: compiled only with
/// HSM_COLLECTOR_TABLES.
///
/// The id is the CRC-32 of the schema's canonical bytes (aicontext/features/api/table-block/feature.md,
/// section "Table schema"; reference: the managed HSMTableBlocks.TableSchema). A collector and the server
/// compute it independently and never exchange it, so the rules and the bytes here must be exactly the
/// managed ones.

#include "hsm_table_block_writer.hpp"

#include <cstdint>
#include <string>
#include <vector>

namespace hsm::collector::tables
{
    /// One column of a schema: its name (UTF-8) and its type.
    struct TableSchemaColumn
    {
        std::string name;
        TableColumnType type;
    };

    /// An ordered list of named, typed columns, the first of which is the time; immutable once created.
    class TableSchema
    {
    public:
        /// The version byte the canonical bytes start with.
        static constexpr std::uint8_t kCanonicalVersion = 1;
        /// The longest column name, in UTF-8 bytes.
        static constexpr std::size_t kMaxNameLength = 255;

        /// Checks the rules and builds the schema: 1 to `max_columns_per_table` columns (at most 255);
        /// the first of type time; every type known; every name non-empty, valid UTF-8 (no surrogate,
        /// no overlong form), free of control characters (U+0000..U+001F, U+007F..U+009F), at most 255
        /// bytes and unique (bytewise, as the managed ordinal comparison). False with `error` naming the
        /// rule otherwise; `out` is then unchanged. No call throws.
        static bool Create(const std::vector<TableSchemaColumn>& columns, std::int32_t max_columns_per_table,
                           TableSchema& out, std::string& error);

        const std::vector<TableSchemaColumn>& Columns() const noexcept { return columns_; }
        const std::vector<std::uint8_t>& CanonicalBytes() const noexcept { return canonical_; }
        /// CRC-32 of CanonicalBytes().
        std::uint32_t Id() const noexcept { return id_; }

    private:
        std::vector<TableSchemaColumn> columns_;
        std::vector<std::uint8_t> canonical_;
        std::uint32_t id_ = 0;
    };
} // namespace hsm::collector::tables
