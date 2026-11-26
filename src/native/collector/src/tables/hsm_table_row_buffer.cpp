#include "hsm_table_row_buffer.hpp"

#include <algorithm>
#include <cstring>
#include <limits>
#include <utility>

namespace hsm::collector::tables
{
    namespace
    {
        std::size_t MapBytes(std::size_t rows) noexcept
        {
            return (rows + 7) / 8;
        }

        bool BitSet(const std::uint8_t* map, std::size_t bit) noexcept
        {
            return (map[bit >> 3] & (1u << (bit & 7))) != 0;
        }

        void SetBit(std::uint8_t* map, std::size_t bit) noexcept
        {
            map[bit >> 3] = static_cast<std::uint8_t>(map[bit >> 3] | (1u << (bit & 7)));
        }
    } // namespace

    TableRowBuffer::TableRowBuffer(std::size_t columns)
        : columns_(columns), values_(columns), nulls_(columns), has_nulls_(columns, false)
    {
    }

    bool TableRowBuffer::IsNull(std::size_t column, std::size_t row) const noexcept
    {
        return has_nulls_[column] && BitSet(nulls_[column].data(), row);
    }

    void TableRowBuffer::Append(const std::int64_t* times_unix_ns, const void* cells, const std::uint8_t* nulls, std::size_t rows)
    {
        if (rows == 0)
            return;

        const std::size_t needed = count_ + rows;
        const bool grows = times_.size() < needed;

        // Which columns get their first null map with these rows.
        std::vector<bool> needs_map;
        bool any_map = false;
        if (nulls != nullptr)
        {
            for (std::size_t column = 0; column < columns_; ++column)
            {
                if (!nulls_[column].empty())
                    continue;
                for (std::size_t row = 0; row < rows; ++row)
                {
                    if (BitSet(nulls, row * columns_ + column))
                    {
                        if (needs_map.empty())
                            needs_map.assign(columns_, false);
                        needs_map[column] = true;
                        any_map = true;
                        break;
                    }
                }
            }
        }

        if (grows || any_map)
        {
            std::size_t capacity = times_.size();
            if (grows)
            {
                capacity = (std::max)(kInitialCapacity, capacity);
                while (capacity < needed)
                    capacity = capacity > (std::numeric_limits<std::size_t>::max)() / 2 ? needed : capacity * 2;
            }

            if (before_growth)
                before_growth();

            // Every new array first; nothing is swapped in until all of them exist.
            std::vector<std::uint64_t> times;
            std::vector<std::vector<std::uint64_t>> values;
            std::vector<std::vector<std::uint8_t>> maps(columns_);
            if (grows)
            {
                times.resize(capacity);
                values.resize(columns_);
                for (std::size_t column = 0; column < columns_; ++column)
                    values[column].resize(capacity);
            }
            for (std::size_t column = 0; column < columns_; ++column)
            {
                const bool has = !nulls_[column].empty();
                if ((has && grows) || (!has && any_map && needs_map[column]))
                    maps[column].resize(MapBytes(capacity));
            }

            // Nothing below allocates: copy and swap.
            if (grows)
            {
                std::copy_n(times_.data(), count_, times.data());
                for (std::size_t column = 0; column < columns_; ++column)
                    std::copy_n(values_[column].data(), count_, values[column].data());
                times_.swap(times);
                values_.swap(values);
            }
            for (std::size_t column = 0; column < columns_; ++column)
            {
                if (maps[column].empty())
                    continue;
                if (!nulls_[column].empty())
                    std::memcpy(maps[column].data(), nulls_[column].data(), MapBytes(count_));
                nulls_[column].swap(maps[column]);
            }
        }

        for (std::size_t row = 0; row < rows; ++row)
        {
            const std::size_t at = count_ + row;
            times_[at] = TicksFromUnixNanoseconds(times_unix_ns[row]);
            const auto* source = static_cast<const unsigned char*>(cells) + row * columns_ * sizeof(std::uint64_t);
            for (std::size_t column = 0; column < columns_; ++column)
            {
                if (nulls != nullptr && BitSet(nulls, row * columns_ + column))
                {
                    values_[column][at] = 0;
                    SetBit(nulls_[column].data(), at);
                    has_nulls_[column] = true;
                }
                else
                {
                    std::uint64_t bits;
                    std::memcpy(&bits, source + column * sizeof bits, sizeof bits);
                    values_[column][at] = bits;
                }
            }
        }

        count_ = needed;
    }

    void TableRowBuffer::Clear() noexcept
    {
        if (times_.size() > kRetainedCapacity)
        {
            std::vector<std::uint64_t>().swap(times_);
            for (std::size_t column = 0; column < columns_; ++column)
            {
                std::vector<std::uint64_t>().swap(values_[column]);
                std::vector<std::uint8_t>().swap(nulls_[column]);
                has_nulls_[column] = false;
            }
            count_ = 0;
            return;
        }

        for (std::size_t column = 0; column < columns_; ++column)
        {
            if (has_nulls_[column])
            {
                std::memset(nulls_[column].data(), 0, MapBytes(count_));
                has_nulls_[column] = false;
            }
        }
        count_ = 0;
    }

    bool TableRowBuffer::CopyNullMap(std::size_t column, std::size_t start, std::size_t count, std::uint8_t* map) const noexcept
    {
        if (!has_nulls_[column])
            return false;

        const std::uint8_t* source = nulls_[column].data();
        bool any = false;
        for (std::size_t row = 0; row < count; ++row)
        {
            if (BitSet(source, start + row))
            {
                SetBit(map, row);
                any = true;
            }
        }
        return any;
    }
} // namespace hsm::collector::tables
