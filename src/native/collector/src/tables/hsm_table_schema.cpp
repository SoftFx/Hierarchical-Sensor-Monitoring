#include "hsm_table_schema.hpp"

#include <algorithm>
#include <cstddef>
#include <new>
#include <set>
#include <utility>

namespace hsm::collector::tables
{
    namespace
    {
        // Decodes `name` as strict UTF-8 (RFC 3629: shortest form, no surrogates, at most U+10FFFF) and
        // checks every code point against the control ranges of .NET char.IsControl. This validates a
        // caller's column name; it reads no bytes that came off the network.
        bool CheckName(const std::string& name, std::size_t index, std::string& error)
        {
            const std::string column = "column " + std::to_string(index);
            if (name.empty())
            {
                error = column + " has no name";
                return false;
            }
            if (name.size() > TableSchema::kMaxNameLength)
            {
                error = "the name of " + column + " is " + std::to_string(name.size()) + " UTF-8 bytes, at most " +
                        std::to_string(TableSchema::kMaxNameLength) + " are allowed";
                return false;
            }

            const auto* bytes = reinterpret_cast<const unsigned char*>(name.data());
            const std::size_t size = name.size();
            for (std::size_t i = 0; i < size;)
            {
                const unsigned lead = bytes[i];
                std::uint32_t code = 0;
                std::size_t length = 0;
                std::uint32_t min = 0;
                if (lead < 0x80)
                {
                    code = lead;
                    length = 1;
                }
                else if (lead >= 0xC2 && lead <= 0xDF)
                {
                    code = lead & 0x1F;
                    length = 2;
                    min = 0x80;
                }
                else if (lead >= 0xE0 && lead <= 0xEF)
                {
                    code = lead & 0x0F;
                    length = 3;
                    min = 0x800;
                }
                else if (lead >= 0xF0 && lead <= 0xF4)
                {
                    code = lead & 0x07;
                    length = 4;
                    min = 0x10000;
                }
                else
                {
                    error = "the name of " + column + " is not valid UTF-8";
                    return false;
                }

                if (i + length > size)
                {
                    error = "the name of " + column + " is not valid UTF-8";
                    return false;
                }
                for (std::size_t k = 1; k < length; ++k)
                {
                    const unsigned next = bytes[i + k];
                    if ((next & 0xC0) != 0x80)
                    {
                        error = "the name of " + column + " is not valid UTF-8";
                        return false;
                    }
                    code = (code << 6) | (next & 0x3F);
                }
                if (code < min || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                {
                    error = "the name of " + column + " is not valid UTF-8";
                    return false;
                }
                if (code <= 0x1F || (code >= 0x7F && code <= 0x9F))
                {
                    error = "the name of " + column + " contains a control character";
                    return false;
                }
                i += length;
            }
            return true;
        }
    } // namespace

    bool TableSchema::Create(const std::vector<TableSchemaColumn>& columns, std::int32_t max_columns_per_table,
                             TableSchema& out, std::string& error)
    {
        try
        {
            const std::int32_t limit = std::min<std::int32_t>(max_columns_per_table, TableBlockFormat::kFormatMaxColumnsPerTable);
            if (columns.empty())
            {
                error = "a table schema needs at least one column";
                return false;
            }
            if (limit < 1 || columns.size() > static_cast<std::size_t>(limit))
            {
                error = "a table schema has at most " + std::to_string(limit) + " columns, this one has " + std::to_string(columns.size());
                return false;
            }

            std::set<std::string> names;
            std::size_t length = 2;
            for (std::size_t i = 0; i < columns.size(); ++i)
            {
                const TableSchemaColumn& column = columns[i];
                if (column.type != TableColumnType::Time && column.type != TableColumnType::Int64 && column.type != TableColumnType::Double)
                {
                    error = "column " + std::to_string(i) + " has unknown type " + std::to_string(static_cast<int>(column.type));
                    return false;
                }
                if (i == 0 && column.type != TableColumnType::Time)
                {
                    error = "the first column of a table schema must be the time";
                    return false;
                }
                if (!CheckName(column.name, i, error))
                    return false;
                if (!names.insert(column.name).second)
                {
                    error = "column name '" + column.name + "' is used twice";
                    return false;
                }
                length += 2 + column.name.size();
            }

            // Canonical bytes: version, column count, then per column its type, its name length and its
            // UTF-8 name. The vector is sized once and written by index: GCC 15 misreads reserve followed by
            // push_back after inlining as a free of an offset pointer (-Wfree-nonheap-object).
            std::vector<std::uint8_t> canonical(length);
            std::size_t at = 0;
            canonical[at++] = kCanonicalVersion;
            canonical[at++] = static_cast<std::uint8_t>(columns.size());
            for (const TableSchemaColumn& column : columns)
            {
                canonical[at++] = static_cast<std::uint8_t>(column.type);
                canonical[at++] = static_cast<std::uint8_t>(column.name.size());
                std::copy(column.name.begin(), column.name.end(), canonical.begin() + static_cast<std::ptrdiff_t>(at));
                at += column.name.size();
            }

            TableSchema schema;
            schema.columns_ = columns;
#if defined(HSM_COLLECTOR_TABLES)
            schema.id_ = TableBlockCrc32(canonical.data(), canonical.size());
#else
            // No zlib in this build, and no block that would carry the id either.
            schema.id_ = 0;
#endif
            schema.canonical_ = std::move(canonical);
            out = std::move(schema);
            return true;
        }
        catch (const std::bad_alloc&)
        {
            error = "out of memory";
            return false;
        }
    }
} // namespace hsm::collector::tables
