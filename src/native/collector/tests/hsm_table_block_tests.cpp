// Tests of the table block writer and schema (#1848). Compiled only with HSM_COLLECTOR_TABLES; the
// test binary's main looks a test up here when it is not one of hsm_collector_tests.cpp.
//
// What proves the writer writes the format: the three golden inputs of the managed
// GoldenBlockTests.cs, restated below, give the header bytes and the decompressed payload of the managed
// golden blocks (src/tests/HSMTableBlocks.Tests/Golden/format1); and native_table_write_vectors writes
// blocks plus their rows as text for the managed NativeBlockVectorTests, which reads every block with
// the managed TableBlockReader.

#include "table_block_test_reader.hpp"
#include "../src/tables/hsm_table_block_writer.hpp"
#include "../src/tables/hsm_table_schema.hpp"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <iterator>
#include <limits>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
    using hsm::collector::tables::TableBlockFormat;
    using hsm::collector::tables::TableBlockLimits;
    using hsm::collector::tables::TableBlockStatus;
    using hsm::collector::tables::TableBlockWriter;
    using hsm::collector::tables::TableColumnType;
    using hsm::collector::tables::TableSchema;
    using hsm::collector::tables::TableSchemaColumn;
    namespace reader = hsm::collector::tables::test;

    void Check(bool condition, const std::string& message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

    const char* StatusName(TableBlockStatus status)
    {
        switch (status)
        {
        case TableBlockStatus::Ok:
            return "Ok";
        case TableBlockStatus::WrongOrder:
            return "WrongOrder";
        case TableBlockStatus::InvalidArgument:
            return "InvalidArgument";
        case TableBlockStatus::LimitExceeded:
            return "LimitExceeded";
        case TableBlockStatus::DuplicateTable:
            return "DuplicateTable";
        case TableBlockStatus::FrameTooLarge:
            return "FrameTooLarge";
        case TableBlockStatus::CompressionFailed:
            return "CompressionFailed";
        case TableBlockStatus::OutOfMemory:
            return "OutOfMemory";
        }
        return "?";
    }

    void CheckStatus(TableBlockStatus actual, TableBlockStatus expected, const std::string& what, const TableBlockWriter& writer)
    {
        if (actual != expected)
            throw std::runtime_error(what + ": expected " + StatusName(expected) + ", got " + StatusName(actual) + " (" + writer.LastError() + ")");
    }

    std::uint64_t Bits(double value)
    {
        std::uint64_t bits;
        std::memcpy(&bits, &value, sizeof bits);
        return bits;
    }

    std::uint64_t Bits(std::int64_t value)
    {
        return static_cast<std::uint64_t>(value);
    }

    std::vector<std::uint8_t> Bytes(const std::string& text)
    {
        return std::vector<std::uint8_t>(text.begin(), text.end());
    }

    std::string Hex(const std::vector<std::uint8_t>& bytes)
    {
        static const char* digits = "0123456789abcdef";
        std::string text;
        for (std::uint8_t b : bytes)
        {
            text += digits[b >> 4];
            text += digits[b & 15];
        }
        return text;
    }

    // ---------------------------------------------------------------------------------------------
    // Tables as the tests describe them (the managed Samples.TestTable).

    struct TestTable
    {
        std::vector<std::uint8_t> id;
        std::uint32_t schema_id = 0;
        std::vector<TableColumnType> types;
        std::vector<std::vector<std::uint64_t>> columns;
        // Per column, the null rows; `nullable[c]` says a map is handed to the writer at all.
        std::vector<std::vector<std::size_t>> null_rows;
        std::vector<bool> nullable;

        std::size_t Rows() const { return columns[0].size(); }

        TestTable& Add(TableColumnType type, std::vector<std::uint64_t> values)
        {
            types.push_back(type);
            columns.push_back(std::move(values));
            null_rows.emplace_back();
            nullable.push_back(false);
            return *this;
        }

        TestTable& WithNulls(std::size_t column, std::vector<std::size_t> rows)
        {
            null_rows[column] = std::move(rows);
            nullable[column] = true;
            return *this;
        }

        bool IsNull(std::size_t column, std::size_t row) const
        {
            return std::find(null_rows[column].begin(), null_rows[column].end(), row) != null_rows[column].end();
        }
    };

    TestTable MakeTable(std::vector<std::uint8_t> id, std::uint32_t schema_id, std::vector<std::int64_t> time)
    {
        TestTable table;
        table.id = std::move(id);
        table.schema_id = schema_id;
        std::vector<std::uint64_t> bits;
        for (std::int64_t t : time)
            bits.push_back(Bits(t));
        table.Add(TableColumnType::Time, std::move(bits));
        return table;
    }

    std::vector<std::uint64_t> Longs(const std::vector<std::int64_t>& values)
    {
        std::vector<std::uint64_t> bits;
        for (std::int64_t v : values)
            bits.push_back(Bits(v));
        return bits;
    }

    std::vector<std::uint64_t> Doubles(const std::vector<double>& values)
    {
        std::vector<std::uint64_t> bits;
        for (double v : values)
            bits.push_back(Bits(v));
        return bits;
    }

    std::vector<std::uint8_t> NullMap(std::size_t rows, const std::vector<std::size_t>& null_rows)
    {
        std::vector<std::uint8_t> map((rows + 7) / 8, 0);
        for (std::size_t row : null_rows)
            map[row >> 3] = static_cast<std::uint8_t>(map[row >> 3] | (1u << (row & 7)));
        return map;
    }

    void AddTables(TableBlockWriter& writer, const std::vector<TestTable>& tables)
    {
        for (const TestTable& table : tables)
        {
            CheckStatus(writer.BeginTable(table.id.data(), table.id.size(), table.schema_id, static_cast<std::int64_t>(table.Rows())),
                        TableBlockStatus::Ok, "BeginTable", writer);
            for (std::size_t c = 0; c < table.columns.size(); ++c)
            {
                std::vector<std::uint8_t> map;
                if (table.nullable[c])
                    map = NullMap(table.Rows(), table.null_rows[c]);
                CheckStatus(writer.AddColumn(table.types[c], table.columns[c].data(), table.columns[c].size(), map.data(), map.size()),
                            TableBlockStatus::Ok, "AddColumn", writer);
            }
        }
    }

    std::vector<std::uint8_t> Write(const std::vector<TestTable>& tables, const TableBlockLimits& limits = TableBlockLimits{})
    {
        TableBlockWriter writer(limits);
        AddTables(writer, tables);
        std::vector<std::uint8_t> block;
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "Finish", writer);
        return block;
    }

    void AssertSame(const std::vector<TestTable>& expected, const reader::ReadBlock& actual, const std::string& what)
    {
        Check(actual.tables.size() == expected.size(), what + ": table count");
        Check(actual.table_count == static_cast<std::int32_t>(expected.size()), what + ": header table count");

        std::size_t rows = 0;
        std::uint64_t first = std::numeric_limits<std::uint64_t>::max();
        std::uint64_t last = 0;
        for (std::size_t t = 0; t < expected.size(); ++t)
        {
            const TestTable& e = expected[t];
            const reader::ReadTable& a = actual.tables[t];
            const std::string where = what + ", table " + std::to_string(t);
            rows += e.Rows();
            for (std::uint64_t time : e.columns[0])
            {
                first = std::min(first, time);
                last = std::max(last, time);
            }

            Check(a.id == e.id, where + ": identifier");
            Check(a.schema_id == e.schema_id, where + ": schema id");
            Check(a.rows == static_cast<std::int32_t>(e.Rows()), where + ": row count");
            Check(a.columns.size() == e.columns.size(), where + ": column count");
            for (std::size_t c = 0; c < e.columns.size(); ++c)
            {
                const reader::ReadColumn& column = a.columns[c];
                const std::string at = where + ", column " + std::to_string(c);
                Check(column.type == e.types[c], at + ": type");
                Check(column.null_map.empty() == e.null_rows[c].empty(), at + ": a map exactly when the column has a null");
                for (std::size_t r = 0; r < e.Rows(); ++r)
                {
                    const bool null = e.IsNull(c, r);
                    Check(column.IsNull(r) == null, at + ", row " + std::to_string(r) + ": null");
                    Check(column.values[r] == (null ? 0 : e.columns[c][r]), at + ", row " + std::to_string(r) + ": value bits");
                }
            }
        }
        Check(actual.row_count == static_cast<std::int32_t>(rows), what + ": header row count");
        Check(actual.first_time == first && actual.last_time == last, what + ": header first and last time");
    }

    std::vector<std::uint8_t> ReadFile(const std::string& path)
    {
        std::ifstream file(path, std::ios::binary);
        Check(file.good(), "cannot open " + path);
        return std::vector<std::uint8_t>(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
    }

    // ---------------------------------------------------------------------------------------------
    // The fixed inputs of the managed golden blocks (GoldenBlockTests.cs, GoldenInputs).

    // SplitMix64, the managed Samples.Rng: the same sequence on every runtime.
    class Rng
    {
    public:
        explicit Rng(std::uint64_t seed)
            : state_(seed)
        {
        }

        std::uint64_t Next()
        {
            std::uint64_t z = (state_ += 0x9E3779B97F4A7C15ULL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
            return z ^ (z >> 31);
        }

        int Next(int max) { return static_cast<int>(Next() % static_cast<std::uint64_t>(max)); }

    private:
        std::uint64_t state_;
    };

    // new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc).Ticks
    constexpr std::int64_t kT0 = 639311184000000000LL;
    constexpr std::int64_t kTicksPerMillisecond = 10000;
    constexpr std::int64_t kMaxTicks = static_cast<std::int64_t>(TableBlockFormat::kMaxTimeTicks);

    double DoubleFromBits(std::uint64_t bits)
    {
        double value;
        std::memcpy(&value, &bits, sizeof value);
        return value;
    }

    // RoundTripTests.ExtremeDoubles. .NET's double.NaN is the negative quiet NaN 0xFFF8000000000000.
    std::vector<std::uint64_t> ExtremeDoubles()
    {
        return {
            Bits(0.0),
            Bits(-0.0),
            Bits(1.0),
            Bits(-1.0),
            0x0000000000000001ULL,
            0x8000000000000001ULL, // double.Epsilon, -double.Epsilon
            0x7FEFFFFFFFFFFFFFULL,
            0xFFEFFFFFFFFFFFFFULL, // double.MaxValue, double.MinValue
            0x7FF0000000000000ULL,
            0xFFF0000000000000ULL, // +Infinity, -Infinity
            0xFFF8000000000000ULL, // double.NaN
            0x7FF0000000000001ULL, // signalling NaN, smallest payload
            0x7FF8DEADBEEF0001ULL, // quiet NaN with a payload
            0xFFFFFFFFFFFFFFFFULL, // negative NaN, all payload bits
            0x000FFFFFFFFFFFFFULL, // largest subnormal
            Bits(1.23456789012345e-300),
            Bits(0.1),
            Bits(1.00001),
        };
    }

    std::vector<std::int64_t> ExtremeLongs()
    {
        return {
            0,
            1,
            -1,
            std::numeric_limits<std::int64_t>::max(),
            std::numeric_limits<std::int64_t>::min(),
            std::numeric_limits<std::int64_t>::max() - 1,
            std::numeric_limits<std::int64_t>::min() + 1,
            std::numeric_limits<std::int32_t>::max(),
            std::numeric_limits<std::int32_t>::min(),
            0x0102030405060708LL,
            static_cast<std::int64_t>(0x8070605040302010ULL),
            42,
            -42,
            std::int64_t{ 1 } << 53,
            (std::int64_t{ 1 } << 53) + 1,
            255,
            256,
            65535,
        };
    }

    std::vector<std::int64_t> ExtremeTimes()
    {
        return {
            0,
            1,
            kMaxTicks,
            kMaxTicks - 1,
            kT0,
            kT0,
            kT0 - 1,
            621355968000000000LL /* Unix epoch */,
            1,
            0,
            kMaxTicks,
            kT0 + 1,
            5,
            4,
            3,
            2,
            1,
            kT0 + 2,
        };
    }

    // Samples.Quotes: time, bid, ask, size from a seed.
    TestTable Quotes(const std::string& id, int rows, std::uint64_t seed, std::uint32_t schema_id = 7)
    {
        Rng rng(seed);
        std::vector<std::int64_t> time;
        std::vector<double> bid;
        std::vector<double> ask;
        std::vector<std::int64_t> size;
        std::int64_t units = 100000 + rng.Next(10000000);
        std::int64_t t = kT0 + rng.Next(10000) * kTicksPerMillisecond;
        for (int i = 0; i < rows; ++i)
        {
            t += (rng.Next(40) - 10) * kTicksPerMillisecond;
            units += rng.Next(21) - 10;
            time.push_back(t);
            bid.push_back(static_cast<double>(units) / 100000.0);
            const std::int64_t spread = units + 1 + rng.Next(5);
            ask.push_back(static_cast<double>(spread) / 100000.0);
            size.push_back(1000LL * (1 + rng.Next(100)));
        }

        TestTable table = MakeTable(Bytes(id), schema_id, time);
        table.Add(TableColumnType::Double, Doubles(bid));
        table.Add(TableColumnType::Double, Doubles(ask));
        table.Add(TableColumnType::Int64, Longs(size));
        return table;
    }

    std::vector<TestTable> GoldenInput(const std::string& name)
    {
        if (name == "mixed")
        {
            std::vector<std::int64_t> reversed = ExtremeTimes();
            std::reverse(reversed.begin(), reversed.end());
            TestTable extremes = MakeTable(Bytes("extremes"), 0xFFFFFFFFu, ExtremeTimes());
            extremes.Add(TableColumnType::Double, ExtremeDoubles());
            extremes.Add(TableColumnType::Int64, Longs(ExtremeLongs()));
            extremes.Add(TableColumnType::Time, Longs(reversed));

            TestTable small = MakeTable({ 0x00, 0xFF, 0x80 }, 0, { kT0, kT0, kT0 - 1 });
            small.Add(TableColumnType::Int64, Longs({ 3, 2, 1 }));

            return { extremes, Quotes("lp1/EURUSD", 20, 0x601D0001), small };
        }
        if (name == "nulls")
        {
            std::vector<std::int64_t> nine;
            std::vector<std::int64_t> tens;
            std::vector<double> quarters;
            for (int i = 0; i < 9; ++i)
            {
                nine.push_back(kT0 + 3 + i);
                tens.push_back(10LL * i);
                quarters.push_back(i / 4.0);
            }
            std::vector<std::int64_t> nine_reversed(nine.rbegin(), nine.rend());

            TestTable a = MakeTable(Bytes("a"), 7, { kT0, kT0 + 1 });
            a.Add(TableColumnType::Double, Doubles({ 1.5, 0.0 })).WithNulls(1, { 1 });

            TestTable b = MakeTable(Bytes("b"), 7, { kT0 + 2 });
            b.Add(TableColumnType::Double, Doubles({ 2.5 }));

            TestTable wide = MakeTable(Bytes("wide"), 9, nine);
            wide.Add(TableColumnType::Int64, Longs(tens));
            wide.Add(TableColumnType::Double, Doubles(quarters));
            wide.Add(TableColumnType::Time, Longs(nine_reversed));
            wide.WithNulls(1, { 0, 8 }).WithNulls(2, { 0, 3, 7 }).WithNulls(3, { 0 });

            TestTable all_null = MakeTable({ 0x00, 0xFF }, 0, { kT0, kT0 - 1, kT0 });
            all_null.Add(TableColumnType::Int64, Longs({ 0, 0, 0 })).WithNulls(1, { 0, 1, 2 });

            return { a, b, wide, Quotes("lp1/EURUSD", 12, 0x601D0002), all_null };
        }
        if (name == "single-row")
        {
            TestTable t = MakeTable(Bytes("t"), 42, { kT0 });
            t.Add(TableColumnType::Double, Doubles({ 1.5 }));
            return { t };
        }
        throw std::runtime_error("unknown golden case " + name);
    }

    // ---------------------------------------------------------------------------------------------
    // Tests.

    void CrcCheckValue()
    {
        const std::string text = "123456789";
        const auto crc = hsm::collector::tables::TableBlockCrc32(reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
        Check(crc == 0xCBF43926u, "CRC-32 of \"123456789\" must be 0xCBF43926");

        // In two parts, as the block checksum is computed (header, then frame).
        auto first = hsm::collector::tables::TableBlockCrc32(reinterpret_cast<const std::uint8_t*>(text.data()), 4);
        first = hsm::collector::tables::TableBlockCrc32(reinterpret_cast<const std::uint8_t*>(text.data()) + 4, 5, first);
        Check(first == 0xCBF43926u, "CRC-32 continued over two parts must equal the one-shot value");
    }

    TableSchema MakeSchema(const std::vector<TableSchemaColumn>& columns)
    {
        TableSchema schema;
        std::string error;
        Check(TableSchema::Create(columns, 64, schema, error), "schema refused: " + error);
        return schema;
    }

    void ExpectSchemaRefused(const std::vector<TableSchemaColumn>& columns, const std::string& what, std::int32_t max_columns = 64)
    {
        TableSchema schema;
        std::string error;
        Check(!TableSchema::Create(columns, max_columns, schema, error), "schema must be refused: " + what);
        Check(!error.empty(), "a refused schema says why: " + what);
    }

    void SchemaIds()
    {
        const TableSchema quotes = MakeSchema({ { "time", TableColumnType::Time },
                                                { "bid", TableColumnType::Double },
                                                { "ask", TableColumnType::Double },
                                                { "size", TableColumnType::Int64 } });
        const std::vector<std::uint8_t> canonical = { 0x01, 0x04, 0x01, 0x04, 't', 'i', 'm', 'e', 0x03, 0x03, 'b', 'i', 'd',
                                                      0x03, 0x03, 'a', 's', 'k', 0x02, 0x04, 's', 'i', 'z', 'e' };
        Check(quotes.CanonicalBytes() == canonical, "canonical bytes of time, bid, ask, size");
        Check(quotes.Id() == 0x8BF94DACu, "schema id of time, bid, ask, size must be 0x8BF94DAC");
        Check(MakeSchema({ { "time", TableColumnType::Time } }).Id() == 0x7FF3E67Fu, "schema id of time alone must be 0x7FF3E67F");

        // A Cyrillic name is its UTF-8 bytes; two names that differ only in case are two names.
        const TableSchema cyrillic = MakeSchema({ { "time", TableColumnType::Time }, { "\xD1\x86\xD0\xB5\xD0\xBD\xD0\xB0", TableColumnType::Double }, { "Bid", TableColumnType::Double }, { "bid", TableColumnType::Double } });
        Check(cyrillic.CanonicalBytes()[9] == 8, "a Cyrillic name of four letters is eight UTF-8 bytes");

        ExpectSchemaRefused({}, "no column");
        ExpectSchemaRefused({ { "bid", TableColumnType::Double } }, "first column not time");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "x", static_cast<TableColumnType>(4) } }, "unknown type");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "", TableColumnType::Double } }, "empty name");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "bid", TableColumnType::Double }, { "bid", TableColumnType::Int64 } }, "duplicate name");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "a\tb", TableColumnType::Double } }, "control character U+0009");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "a\x7F", TableColumnType::Double } }, "control character U+007F");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "a\xC2\x85", TableColumnType::Double } }, "control character U+0085");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "a\xFF", TableColumnType::Double } }, "invalid UTF-8");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "\xC0\xAF", TableColumnType::Double } }, "overlong UTF-8");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "\xED\xA0\x80", TableColumnType::Double } }, "a surrogate in UTF-8");
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { std::string(256, 'n'), TableColumnType::Double } }, "a name of 256 bytes");
        MakeSchema({ { "time", TableColumnType::Time }, { std::string(255, 'n'), TableColumnType::Double } });
        ExpectSchemaRefused({ { "time", TableColumnType::Time }, { "a", TableColumnType::Double }, { "b", TableColumnType::Double } }, "more columns than the limit", 2);
    }

    // The worked example of the specification: 77 bytes with the null, 76 without.
    void WorkedExample()
    {
        TestTable a = MakeTable(Bytes("a"), 7, { kT0, kT0 + 1 });
        a.Add(TableColumnType::Double, Doubles({ 1.5, 123.0 })).WithNulls(1, { 1 });
        TestTable b = MakeTable(Bytes("b"), 7, { kT0 + 2 });
        b.Add(TableColumnType::Double, Doubles({ 2.5 }));

        std::vector<std::uint8_t> expected = { 0x01, 0x00, 'a', 0x07, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x01, 0x83,
                                               0x01, 0x00, 'b', 0x07, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x01, 0x03 };
        const auto append = [&expected](std::uint64_t value) {
            for (int i = 0; i < 8; ++i)
                expected.push_back(static_cast<std::uint8_t>(value >> (8 * i)));
        };
        append(Bits(kT0));
        append(Bits(kT0 + 1));
        append(Bits(kT0 + 2));
        append(Bits(1.5)); // 00 00 00 00 00 00 F8 3F
        append(0);         // row 1 of a is null: eight zero bytes, whatever the caller left there
        append(Bits(2.5)); // 00 00 00 00 00 00 04 40
        expected.push_back(0x02);
        Check(expected.size() == 77, "the worked example is 77 bytes");

        const auto block = Write({ a, b });
        const auto read = reader::ReadTableBlock(block);
        Check(read.payload == expected, "the payload of the worked example, byte for byte: " + Hex(read.payload));
        AssertSame({ a, b }, read, "worked example");

        // Without the null: 0x03 in place of 0x83, the value in place of the zeros, no map.
        TestTable a_full = MakeTable(Bytes("a"), 7, { kT0, kT0 + 1 });
        a_full.Add(TableColumnType::Double, Doubles({ 1.5, -0.0 }));
        auto without = expected;
        without[13] = 0x03;
        for (int i = 0; i < 8; ++i)
            without[28 + 24 + 8 + i] = static_cast<std::uint8_t>(Bits(-0.0) >> (8 * i));
        without.pop_back();
        Check(reader::ReadTableBlock(Write({ a_full, b })).payload == without, "the worked example without its null is 76 bytes");
    }

    void Golden(const std::string& path)
    {
        const std::string name = std::filesystem::path(path).stem().string();
        const auto committed = ReadFile(path);

        // The test reader against the managed writer's block: it must accept it and decode the input.
        const auto input = GoldenInput(name);
        const auto golden = reader::ReadTableBlock(committed);
        AssertSame(input, golden, "managed golden " + name);

        // The native writer: header fields and decompressed payload equal the managed golden block's.
        const auto written = Write(input);
        Check(std::equal(committed.begin(), committed.begin() + 32, written.begin()), name + ": header bytes 0..31 differ from the managed golden block");
        Check(std::equal(committed.begin() + 36, committed.begin() + 40, written.begin() + 36), name + ": the decompressed length differs");
        const auto native = reader::ReadTableBlock(written);
        Check(native.payload == golden.payload, name + ": the decompressed payload differs from the managed golden block");
        AssertSame(input, native, "native " + name);
    }

    void ExpectRefused(TableBlockStatus actual, TableBlockStatus expected, const std::string& what, const TableBlockWriter& writer)
    {
        CheckStatus(actual, expected, what, writer);
        Check(actual == TableBlockStatus::Ok || !writer.LastError().empty(), what + ": a refusal says why");
    }

    void LimitsAndRulesRefused()
    {
        const std::vector<std::uint64_t> two_times = Longs({ kT0, kT0 + 1 });
        const std::vector<std::uint64_t> two_values = Doubles({ 1.5, 2.5 });
        const std::uint8_t a = 'a';
        const std::uint8_t b = 'b';
        const std::uint8_t c = 'c';
        std::vector<std::uint8_t> out;

        {
            TableBlockLimits limits;
            limits.max_tables = 2;
            TableBlockWriter writer(limits);
            CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table 1", writer);
            CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time 1", writer);
            CheckStatus(writer.BeginTable(&b, 1, 1, 2), TableBlockStatus::Ok, "table 2", writer);
            CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time 2", writer);
            ExpectRefused(writer.BeginTable(&c, 1, 1, 2), TableBlockStatus::LimitExceeded, "a third table over max_tables 2", writer);
            // Nothing was written: the block of two tables is whole.
            CheckStatus(writer.Finish(out), TableBlockStatus::Ok, "Finish after a refused table", writer);
            Check(reader::ReadTableBlock(out).tables.size() == 2, "the refused table left no trace");
        }
        {
            TableBlockLimits limits;
            limits.max_rows = 3;
            TableBlockWriter writer(limits);
            CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table 1", writer);
            CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time", writer);
            ExpectRefused(writer.BeginTable(&b, 1, 1, 2), TableBlockStatus::LimitExceeded, "rows over max_rows 3", writer);
        }
        {
            TableBlockWriter writer;
            ExpectRefused(writer.BeginTable(&a, 1, 1, std::int64_t{ 1 } << 31), TableBlockStatus::LimitExceeded, "a row count outside int32", writer);
            ExpectRefused(writer.BeginTable(&a, 1, 1, 0), TableBlockStatus::InvalidArgument, "a table without rows", writer);
            ExpectRefused(writer.BeginTable(&a, 0, 1, 1), TableBlockStatus::InvalidArgument, "an empty identifier", writer);
            ExpectRefused(writer.BeginTable(nullptr, 1, 1, 1), TableBlockStatus::InvalidArgument, "no identifier", writer);
        }
        {
            TableBlockLimits limits;
            limits.max_columns_per_table = 2;
            TableBlockWriter writer(limits);
            CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table", writer);
            CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time", writer);
            CheckStatus(writer.AddColumn(TableColumnType::Double, two_values.data(), 2), TableBlockStatus::Ok, "column 2", writer);
            ExpectRefused(writer.AddColumn(TableColumnType::Int64, two_times.data(), 2), TableBlockStatus::LimitExceeded, "a third column over 2", writer);
        }
        {
            TableBlockLimits limits;
            limits.max_table_id_length = 4;
            TableBlockWriter writer(limits);
            const std::vector<std::uint8_t> five = Bytes("fives");
            ExpectRefused(writer.BeginTable(five.data(), five.size(), 1, 1), TableBlockStatus::LimitExceeded, "an identifier over 4 bytes", writer);
            CheckStatus(writer.BeginTable(five.data(), 4, 1, 1), TableBlockStatus::Ok, "an identifier of exactly 4 bytes", writer);
        }
        {
            // The exact payload length: "a" with time and double over two rows is 14 + 32 = 46 bytes,
            // with a null 47.
            for (std::int64_t limit : { 46, 45 })
            {
                TableBlockLimits limits;
                limits.max_decompressed_bytes = limit;
                TableBlockWriter writer(limits);
                CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table", writer);
                CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time", writer);
                ExpectRefused(writer.AddColumn(TableColumnType::Double, two_values.data(), 2),
                              limit == 46 ? TableBlockStatus::Ok : TableBlockStatus::LimitExceeded, "a payload of 46 bytes under a limit of " + std::to_string(limit), writer);
                Check(writer.DecompressedLength() == (limit == 46 ? 46 : 29), "the exact decompressed length");
            }
            for (std::int64_t limit : { 47, 46 })
            {
                TableBlockLimits limits;
                limits.max_decompressed_bytes = limit;
                TableBlockWriter writer(limits);
                const std::uint8_t map = 0x02;
                CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table", writer);
                CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::Ok, "time", writer);
                ExpectRefused(writer.AddColumn(TableColumnType::Double, two_values.data(), 2, &map, 1),
                              limit == 47 ? TableBlockStatus::Ok : TableBlockStatus::LimitExceeded, "a payload of 47 bytes with its map under a limit of " + std::to_string(limit), writer);
            }
            TableBlockLimits limits;
            limits.max_decompressed_bytes = 20;
            TableBlockWriter writer(limits);
            ExpectRefused(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::LimitExceeded, "a table whose time column alone passes the payload limit", writer);
        }
        {
            TableBlockWriter writer;
            ExpectRefused(writer.AddColumn(TableColumnType::Time, two_times.data(), 2), TableBlockStatus::WrongOrder, "a column before BeginTable", writer);
            ExpectRefused(writer.Finish(out), TableBlockStatus::WrongOrder, "a block without tables", writer);
            CheckStatus(writer.BeginTable(&a, 1, 1, 2), TableBlockStatus::Ok, "table", writer);
            ExpectRefused(writer.Finish(out), TableBlockStatus::WrongOrder, "a table without columns", writer);
            ExpectRefused(writer.BeginTable(&b, 1, 1, 2), TableBlockStatus::WrongOrder, "a table after one without columns", writer);
            ExpectRefused(writer.AddColumn(TableColumnType::Double, two_values.data(), 2), TableBlockStatus::WrongOrder, "a first column that is not time", writer);
            ExpectRefused(writer.AddColumn(static_cast<TableColumnType>(4), two_times.data(), 2), TableBlockStatus::InvalidArgument, "an unknown type", writer);
            ExpectRefused(writer.AddColumn(TableColumnType::Time, two_times.data(), 1), TableBlockStatus::InvalidArgument, "a column of the wrong length", writer);
            ExpectRefused(writer.AddColumn(TableColumnType::Time, nullptr, 2), TableBlockStatus::InvalidArgument, "a column without values", writer);
            const std::uint8_t null_row = 0x01;
            ExpectRefused(writer.AddColumn(TableColumnType::Time, two_times.data(), 2, &null_row, 1), TableBlockStatus::InvalidArgument, "a null time", writer);
            const std::uint8_t no_null = 0x00;
            CheckStatus(writer.AddColumn(TableColumnType::Time, two_times.data(), 2, &no_null, 1), TableBlockStatus::Ok, "a time map that marks no row is no map", writer);
            const std::uint8_t padding = 0x04;
            ExpectRefused(writer.AddColumn(TableColumnType::Double, two_values.data(), 2, &padding, 1), TableBlockStatus::InvalidArgument, "padding bits in the map", writer);
            const std::uint8_t two_bytes[2] = { 0x01, 0x00 };
            ExpectRefused(writer.AddColumn(TableColumnType::Double, two_values.data(), 2, two_bytes, 2), TableBlockStatus::InvalidArgument, "a map of the wrong length", writer);
            const std::vector<std::uint64_t> outside = { Bits(kT0), TableBlockFormat::kMaxTimeTicks + 1 };
            ExpectRefused(writer.AddColumn(TableColumnType::Time, outside.data(), 2), TableBlockStatus::InvalidArgument, "a later time column past DateTime.MaxValue", writer);
            const std::vector<std::uint64_t> negative = { Bits(kT0), Bits(std::int64_t{ -1 }) };
            ExpectRefused(writer.AddColumn(TableColumnType::Time, negative.data(), 2), TableBlockStatus::InvalidArgument, "a negative time", writer);
            // A null row of a later time column is not range-checked: its cell is written as zero.
            ExpectRefused(writer.AddColumn(TableColumnType::Time, outside.data(), 2, two_bytes + 1, 0), TableBlockStatus::InvalidArgument, "an empty map is no map", writer);
            const std::uint8_t second_null = 0x02;
            CheckStatus(writer.AddColumn(TableColumnType::Time, outside.data(), 2, &second_null, 1), TableBlockStatus::Ok, "a null time cell out of range", writer);
            CheckStatus(writer.Finish(out), TableBlockStatus::Ok, "Finish after every refusal", writer);
            const auto read = reader::ReadTableBlock(out);
            Check(read.tables[0].columns.size() == 2 && read.tables[0].columns[1].values[1] == 0, "the null time cell holds zero");
        }
        {
            TestTable one = MakeTable(Bytes("same"), 1, { kT0 });
            TestTable two = MakeTable(Bytes("same"), 2, { kT0 });
            TableBlockWriter writer;
            AddTables(writer, { one, MakeTable(Bytes("other"), 1, { kT0 }), two });
            ExpectRefused(writer.Finish(out), TableBlockStatus::DuplicateTable, "two tables with one identifier", writer);
            Check(out.empty(), "a refused block leaves nothing in out");
        }
        {
            TableBlockLimits limits;
            limits.max_columns_per_table = 256;
            TableBlockWriter writer(limits);
            ExpectRefused(writer.BeginTable(&a, 1, 1, 1), TableBlockStatus::InvalidArgument, "limits outside the format", writer);
        }
    }

    void NullsWrittenAsTheFormatSays()
    {
        // A map with no bit set is no map: the same payload as a column without nulls.
        TestTable plain = MakeTable(Bytes("q"), 3, { kT0, kT0 + 1, kT0 + 2 });
        plain.Add(TableColumnType::Int64, Longs({ 1, 2, 3 }));
        TestTable zero_map = plain;
        zero_map.WithNulls(1, {});
        Check(reader::ReadTableBlock(Write({ plain })).payload == reader::ReadTableBlock(Write({ zero_map })).payload,
              "an all-zero map writes exactly what no map writes");

        // Values under nulls are dropped; a column null in every row; a nine-row map with padding.
        TestTable nulls = MakeTable(Bytes("n"), 3, { kT0, kT0, kT0, kT0, kT0, kT0, kT0, kT0, kT0 });
        nulls.Add(TableColumnType::Double, Doubles({ 1, 2, 3, 4, 5, 6, 7, 8, 9 })).WithNulls(1, { 0, 8 });
        nulls.Add(TableColumnType::Int64, Longs({ 9, 9, 9, 9, 9, 9, 9, 9, 9 })).WithNulls(2, { 0, 1, 2, 3, 4, 5, 6, 7, 8 });
        nulls.Add(TableColumnType::Int64, Longs({ 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        const auto read = reader::ReadTableBlock(Write({ nulls, plain }));
        AssertSame({ nulls, plain }, read, "nulls");
        Check(read.tables[0].columns[1].null_map == std::vector<std::uint8_t>({ 0x01, 0x01 }), "a two-byte map, row 8 in the second byte");
        Check(read.tables[0].columns[2].null_map == std::vector<std::uint8_t>({ 0xFF, 0x01 }), "a column null in every row");
        Check(read.tables[0].columns[3].null_map.empty(), "a column without a null carries no map");
    }

    void NanAndInfinitiesBitExact()
    {
        TestTable table = MakeTable(Bytes("extremes"), 0xFFFFFFFFu, ExtremeTimes());
        table.Add(TableColumnType::Double, ExtremeDoubles());
        table.Add(TableColumnType::Int64, Longs(ExtremeLongs()));
        const auto read = reader::ReadTableBlock(Write({ table }));
        AssertSame({ table }, read, "extremes");
        Check(read.first_time == 0 && read.last_time == TableBlockFormat::kMaxTimeTicks, "first time 0, last time DateTime.MaxValue");

        // Through AddDoubleColumn, from doubles and not from bits.
        std::vector<double> doubles;
        for (std::uint64_t bits : ExtremeDoubles())
            doubles.push_back(DoubleFromBits(bits));
        const std::vector<std::uint64_t> times = Longs(ExtremeTimes());
        TableBlockWriter writer;
        std::vector<std::uint8_t> block;
        CheckStatus(writer.BeginTable(table.id.data(), table.id.size(), table.schema_id, static_cast<std::int64_t>(times.size())), TableBlockStatus::Ok, "table", writer);
        CheckStatus(writer.AddColumn(TableColumnType::Time, times.data(), times.size()), TableBlockStatus::Ok, "time", writer);
        CheckStatus(writer.AddDoubleColumn(doubles.data(), doubles.size()), TableBlockStatus::Ok, "doubles", writer);
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "Finish", writer);
        Check(reader::ReadTableBlock(block).tables[0].columns[1].values == ExtremeDoubles(), "NaN payloads, infinities and -0.0 keep their bits through AddDoubleColumn");
    }

    // A deterministic mix of tables: random identifiers, column counts and types, NaN payloads, nulls.
    std::vector<TestTable> RandomTables(std::uint64_t seed, int max_tables, int max_rows)
    {
        Rng rng(seed);
        const std::vector<std::uint64_t> specials = ExtremeDoubles();
        const int count = 1 + rng.Next(max_tables);
        std::vector<TestTable> tables;
        for (int t = 0; t < count; ++t)
        {
            std::vector<std::uint8_t> id(static_cast<std::size_t>(1 + rng.Next(12)));
            for (std::uint8_t& b : id)
                b = static_cast<std::uint8_t>(rng.Next(256));
            // The table's index keeps identifiers unique.
            for (int k = 0; k < 4; ++k)
                id.push_back(static_cast<std::uint8_t>(t >> (8 * k)));

            const std::size_t rows = static_cast<std::size_t>(1 + rng.Next(max_rows));
            std::vector<std::int64_t> time;
            for (std::size_t r = 0; r < rows; ++r)
                time.push_back(rng.Next(4) == 0 ? static_cast<std::int64_t>(rng.Next() % (TableBlockFormat::kMaxTimeTicks + 1)) : kT0 + rng.Next(100000));
            TestTable table = MakeTable(std::move(id), static_cast<std::uint32_t>(rng.Next()), time);

            const int columns = rng.Next(8);
            for (int c = 1; c <= columns; ++c)
            {
                const int kind = rng.Next(3);
                std::vector<std::uint64_t> values;
                for (std::size_t r = 0; r < rows; ++r)
                {
                    if (kind == 0)
                        values.push_back(rng.Next() % (TableBlockFormat::kMaxTimeTicks + 1));
                    else if (kind == 2 && rng.Next(3) == 0)
                        values.push_back(specials[static_cast<std::size_t>(rng.Next(static_cast<int>(specials.size())))]);
                    else
                        values.push_back(rng.Next());
                }
                table.Add(kind == 0 ? TableColumnType::Time : kind == 1 ? TableColumnType::Int64
                                                                        : TableColumnType::Double,
                          std::move(values));
                if (rng.Next(3) == 0)
                {
                    std::vector<std::size_t> null_rows;
                    for (std::size_t r = 0; r < rows; ++r)
                        if (rng.Next(4) == 0)
                            null_rows.push_back(r);
                    table.WithNulls(static_cast<std::size_t>(c), std::move(null_rows));
                }
            }
            tables.push_back(std::move(table));
        }
        return tables;
    }

    std::vector<TestTable> ThousandTables()
    {
        std::vector<TestTable> tables;
        for (int i = 0; i < 1000; ++i)
        {
            TestTable table = Quotes("lp" + std::to_string(i % 7) + "/S" + std::to_string(i), 1 + i % 26, 0x7AB1E000u + static_cast<std::uint64_t>(i));
            if (i % 50 == 0)
                table.WithNulls(2, { 0 });
            tables.push_back(std::move(table));
        }
        return tables;
    }

    void SeededTablesRoundTrip()
    {
        for (std::uint64_t seed = 1; seed <= 200; ++seed)
        {
            const auto tables = RandomTables(seed, 12, 40);
            AssertSame(tables, reader::ReadTableBlock(Write(tables)), "seed " + std::to_string(seed));
        }
    }

    void ThousandTablesRoundTrip()
    {
        const auto tables = ThousandTables();
        const auto read = reader::ReadTableBlock(Write(tables));
        AssertSame(tables, read, "a thousand tables");
        Check(read.tables.size() == 1000, "a thousand tables in one block");
    }

    void BlockOverFrameLimitIsRefused()
    {
        const auto tables = RandomTables(77, 30, 200);
        TableBlockWriter writer;
        AddTables(writer, tables);

        std::vector<std::uint8_t> block;
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "Finish without a frame limit", writer);
        const std::size_t size = block.size();

        std::vector<std::uint8_t> framed;
        CheckStatus(writer.Finish(framed, 40, size + 40), TableBlockStatus::Ok, "a block that just fits the frame limit", writer);
        Check(framed.size() == size + 40, "the reserved front bytes come before the block");
        Check(std::all_of(framed.begin(), framed.begin() + 40, [](std::uint8_t b) { return b == 0; }), "the reserved bytes are zero");
        Check(std::equal(block.begin(), block.end(), framed.begin() + 40), "the same block after the reserved bytes");

        ExpectRefused(writer.Finish(framed, 40, size + 39), TableBlockStatus::FrameTooLarge, "a block one byte over the frame limit", writer);
        Check(framed.empty(), "a block over the frame limit leaves nothing in out");
        Check(writer.LastError().find("split") != std::string::npos, "the refusal says the block must be split");

        // The writer keeps its tables: the caller can still take them (PR 3 splits them).
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "Finish again after the refusal", writer);
        AssertSame(tables, reader::ReadTableBlock(block), "the block after a refused frame");
    }

    void WriterIsReusable()
    {
        const auto first = RandomTables(5, 10, 30);
        const auto second = RandomTables(6, 10, 30);
        TableBlockWriter writer;
        std::vector<std::uint8_t> block;
        AddTables(writer, first);
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "first block", writer);
        writer.Reset();
        AddTables(writer, second);
        CheckStatus(writer.Finish(block), TableBlockStatus::Ok, "second block", writer);
        Check(block == Write(second), "a reused writer writes the block a new writer writes");
        AssertSame(second, reader::ReadTableBlock(block), "reused writer");
    }

    void ExpectReadRefused(const std::vector<std::uint8_t>& block, reader::TableBlockError error, const std::string& what)
    {
        try
        {
            reader::ReadTableBlock(block);
        }
        catch (const reader::TableBlockReadError& ex)
        {
            Check(ex.Error() == error, what + ": expected " + reader::ErrorName(error) + ", got " + ex.what());
            return;
        }
        throw std::runtime_error(what + ": the test reader accepted a damaged block");
    }

    void ResealCrc(std::vector<std::uint8_t>& block)
    {
        std::uint32_t crc = hsm::collector::tables::TableBlockCrc32(block.data(), 40);
        crc = hsm::collector::tables::TableBlockCrc32(block.data() + 44, block.size() - 44, crc);
        for (int i = 0; i < 4; ++i)
            block[40 + static_cast<std::size_t>(i)] = static_cast<std::uint8_t>(crc >> (8 * i));
    }

    // The test reader is strict: a few faults, one per case, with the managed error codes.
    void TestReaderIsStrict()
    {
        const auto good = Write(GoldenInput("nulls"));
        reader::ReadTableBlock(good);

        auto damaged = good;
        damaged[40] ^= 1;
        ExpectReadRefused(damaged, reader::TableBlockError::ChecksumMismatch, "a damaged checksum");
        damaged = good;
        damaged.back() ^= 0x55;
        ExpectReadRefused(damaged, reader::TableBlockError::ChecksumMismatch, "a damaged frame");
        damaged = good;
        damaged.pop_back();
        ExpectReadRefused(damaged, reader::TableBlockError::Truncated, "a truncated block");
        damaged = good;
        damaged.push_back(0);
        ExpectReadRefused(damaged, reader::TableBlockError::LengthMismatch, "a byte after the frame");
        damaged = good;
        damaged[0] = 'X';
        ExpectReadRefused(damaged, reader::TableBlockError::BadMagic, "bad magic");
        damaged = good;
        damaged[4] = 2;
        ExpectReadRefused(damaged, reader::TableBlockError::UnknownVersion, "version 2");
        damaged = good;
        damaged[5] = 1;
        ResealCrc(damaged);
        ExpectReadRefused(damaged, reader::TableBlockError::CorruptHeader, "a reserved byte");
        damaged = good;
        damaged[8] = static_cast<std::uint8_t>(damaged[8] + 1);
        ResealCrc(damaged);
        ExpectReadRefused(damaged, reader::TableBlockError::HeaderMismatch, "a row count the directory does not have");
        damaged = good;
        damaged[16] = static_cast<std::uint8_t>(damaged[16] - 1);
        ResealCrc(damaged);
        ExpectReadRefused(damaged, reader::TableBlockError::HeaderMismatch, "a first time the columns do not have");
        TableBlockLimits small;
        small.max_rows = 10;
        try
        {
            reader::ReadTableBlock(good, small);
            throw std::runtime_error("the test reader ignored its row limit");
        }
        catch (const reader::TableBlockReadError& ex)
        {
            Check(ex.Error() == reader::TableBlockError::LimitExceeded, std::string("a row limit: ") + ex.what());
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Vectors for the managed reader (NativeBlockVectorTests, HSM_NATIVE_TABLE_BLOCKS_DIR).

    // <name>.rows.txt: per table "table <id hex> <schema id> <rows> <type,type,...>", then one line per
    // column "col" followed by each row's value as 16 hex digits of its bits, or "-" for a null.
    std::string RowsText(const std::vector<TestTable>& tables)
    {
        std::ostringstream text;
        text << "tables " << tables.size() << '\n';
        for (const TestTable& table : tables)
        {
            text << "table " << Hex(table.id) << ' ' << table.schema_id << ' ' << table.Rows() << ' ';
            for (std::size_t c = 0; c < table.types.size(); ++c)
                text << (c == 0 ? "" : ",") << static_cast<int>(table.types[c]);
            text << '\n';
            for (std::size_t c = 0; c < table.columns.size(); ++c)
            {
                text << "col";
                for (std::size_t r = 0; r < table.Rows(); ++r)
                {
                    if (table.IsNull(c, r))
                    {
                        text << " -";
                        continue;
                    }
                    char digits[17];
                    std::snprintf(digits, sizeof digits, "%016llx", static_cast<unsigned long long>(table.columns[c][r]));
                    text << ' ' << digits;
                }
                text << '\n';
            }
        }
        return text.str();
    }

    void WriteVectors(const std::string& directory)
    {
        Check(!directory.empty(), "pass the directory to write the vectors into");
        std::filesystem::create_directories(directory);

        std::vector<std::pair<std::string, std::vector<TestTable>>> cases = {
            { "golden-mixed", GoldenInput("mixed") },
            { "golden-nulls", GoldenInput("nulls") },
            { "golden-single-row", GoldenInput("single-row") },
            { "thousand-tables", ThousandTables() },
        };
        for (std::uint64_t seed = 1; seed <= 24; ++seed)
            cases.emplace_back("seeded-" + std::to_string(seed), RandomTables(0x5EED0000u + seed, seed % 4 == 0 ? 300 : 20, seed % 3 == 0 ? 400 : 30));

        for (const auto& [name, tables] : cases)
        {
            const auto block = Write(tables);
            AssertSame(tables, reader::ReadTableBlock(block), name);

            const auto base = std::filesystem::path(directory) / name;
            std::ofstream binary(base.string() + ".htb", std::ios::binary | std::ios::trunc);
            binary.write(reinterpret_cast<const char*>(block.data()), static_cast<std::streamsize>(block.size()));
            std::ofstream rows(base.string() + ".rows.txt", std::ios::binary | std::ios::trunc);
            rows << RowsText(tables);
            Check(binary.good() && rows.good(), "cannot write the vector " + name);
        }
        std::cout << "wrote " << cases.size() << " table block vectors to " << directory << '\n';
    }
} // namespace

const std::map<std::string, std::function<void(const std::string&)>>& TableBlockTests()
{
    static const std::map<std::string, std::function<void(const std::string&)>> tests = {
        { "native_table_crc_check_value", [](const std::string&) { CrcCheckValue(); } },
        { "native_table_schema_ids", [](const std::string&) { SchemaIds(); } },
        { "native_table_worked_example", [](const std::string&) { WorkedExample(); } },
        { "native_table_golden", [](const std::string& path) { Golden(path); } },
        { "native_table_limits_and_rules_refused", [](const std::string&) { LimitsAndRulesRefused(); } },
        { "native_table_nulls", [](const std::string&) { NullsWrittenAsTheFormatSays(); } },
        { "native_table_nan_and_infinities_bit_exact", [](const std::string&) { NanAndInfinitiesBitExact(); } },
        { "native_table_seeded_round_trips", [](const std::string&) { SeededTablesRoundTrip(); } },
        { "native_table_thousand_tables", [](const std::string&) { ThousandTablesRoundTrip(); } },
        { "native_table_block_over_frame_limit_is_refused", [](const std::string&) { BlockOverFrameLimitIsRefused(); } },
        { "native_table_writer_is_reusable", [](const std::string&) { WriterIsReusable(); } },
        { "native_table_test_reader_is_strict", [](const std::string&) { TestReaderIsStrict(); } },
        { "native_table_write_vectors", [](const std::string& directory) { WriteVectors(directory); } },
    };
    return tests;
}
