using System.Text;
using HSMTableBlocks;
using Xunit;
using T = HSMTableBlocks.TableColumnType;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// <see cref="TableSchema"/>: its rules, its canonical bytes and its id (pinned against zlib's crc32, which is what a
    /// C++ writer computes it with); and <see cref="TableBlockReader.ReadDirectory"/>. That ReadDirectory refuses
    /// exactly what Read refuses is checked by every malformed-block case (<see cref="Samples.AssertRefused"/>) and by
    /// the fuzz test.
    /// </summary>
    public class SchemaAndDirectoryTests
    {
        private static TableSchema Schema(params (string Name, T Type)[] columns) =>
            new TableSchema(columns.Select(c => new TableSchemaColumn(c.Name, c.Type)));

        private static readonly (string, T)[] Quotes = { ("time", T.Time), ("bid", T.Double), ("ask", T.Double), ("size", T.Int64) };


        [Fact]
        public void The_canonical_bytes_and_the_id_are_pinned()
        {
            var schema = Schema(Quotes);

            var expected = new byte[] { 1, 4, 1, 4 }.Concat(Encoding.ASCII.GetBytes("time"))
                .Concat(new byte[] { 3, 3 }).Concat(Encoding.ASCII.GetBytes("bid"))
                .Concat(new byte[] { 3, 3 }).Concat(Encoding.ASCII.GetBytes("ask"))
                .Concat(new byte[] { 2, 4 }).Concat(Encoding.ASCII.GetBytes("size")).ToArray();

            Assert.Equal(expected, schema.CanonicalBytes);
            // zlib.crc32 of the bytes above (computed outside .NET).
            Assert.Equal(0x8BF94DACu, schema.Id);

            Assert.Equal(0x7FF3E67Fu, Schema(("time", T.Time)).Id);
        }

        [Fact]
        public void Names_are_encoded_as_UTF8_bytes()
        {
            var schema = Schema(("Время", T.Time), ("p", T.Double));

            Assert.Equal(10, schema.CanonicalBytes[3]);
            Assert.Equal(Encoding.UTF8.GetBytes("Время"), schema.CanonicalBytes.Skip(4).Take(10).ToArray());
            Assert.Equal(0xA5D86960u, schema.Id);
        }

        [Fact]
        public void The_id_changes_with_every_name_type_and_order()
        {
            var ids = new[]
            {
                Schema(Quotes).Id,
                Schema(("time", T.Time), ("bid", T.Double), ("ask", T.Double), ("size", T.Double)).Id,
                Schema(("time", T.Time), ("bid", T.Double), ("ask", T.Double), ("Size", T.Int64)).Id,
                Schema(("time", T.Time), ("ask", T.Double), ("bid", T.Double), ("size", T.Int64)).Id,
                Schema(("time", T.Time), ("bid", T.Double), ("ask", T.Double)).Id,
            };

            Assert.Equal(ids.Length, ids.Distinct().Count());
        }

        [Fact]
        public void Same_schema_and_column_types()
        {
            var a = Schema(Quotes);
            var b = Schema(Quotes);
            var renamed = Schema(("time", T.Time), ("bid", T.Double), ("ask", T.Double), ("volume", T.Int64));

            Assert.True(a.SameAs(b));
            Assert.Equal(a.Id, b.Id);
            Assert.False(a.SameAs(renamed));
            Assert.False(a.SameAs(null));

            Assert.True(a.HasColumnTypes(new[] { T.Time, T.Double, T.Double, T.Int64 }));
            Assert.True(a.HasColumnTypes(renamed.ColumnTypes));
            Assert.False(a.HasColumnTypes(new[] { T.Time, T.Double, T.Double }));
            Assert.False(a.HasColumnTypes(new[] { T.Time, T.Double, T.Int64, T.Int64 }));
            Assert.False(a.HasColumnTypes(null));
        }

        public static TheoryData<string, (string, T)[]> Refused => new()
        {
            { "no columns", new (string, T)[0] },
            { "first not time", new[] { ("bid", T.Double), ("time", T.Time) } },
            { "unknown type", new[] { ("time", T.Time), ("x", (T)4) } },
            { "zero type", new[] { ("time", T.Time), ("x", (T)0) } },
            { "empty name", new[] { ("time", T.Time), ("", T.Double) } },
            { "null name", new[] { ("time", T.Time), ((string)null, T.Double) } },
            { "duplicate name", new[] { ("time", T.Time), ("bid", T.Double), ("bid", T.Int64) } },
            { "control character", new[] { ("time", T.Time), ("b\nid", T.Double) } },
            { "lone surrogate", new[] { ("time", T.Time), ("b\uD800", T.Double) } },
            { "name too long", new[] { ("time", T.Time), (new string('n', 256), T.Double) } },
            { "multi-byte name too long", new[] { ("time", T.Time), (new string('я', 128), T.Double) } },
        };

        [Theory]
        [MemberData(nameof(Refused))]
        public void A_schema_outside_the_rules_is_refused(string why, (string, T)[] columns)
        {
            Assert.Throws<ArgumentException>(() => Schema(columns));
            Assert.NotNull(why);
        }

        [Fact]
        public void Column_count_follows_the_limits()
        {
            var columns = new[] { new TableSchemaColumn("time", T.Time) }
                .Concat(Enumerable.Range(1, TableBlockLimits.DefaultMaxColumnsPerTable - 1).Select(i => new TableSchemaColumn($"c{i}", T.Double)))
                .ToList();

            Assert.Equal(TableBlockLimits.DefaultMaxColumnsPerTable, new TableSchema(columns).Columns.Count);

            columns.Add(new TableSchemaColumn("one more", T.Double));
            Assert.Throws<ArgumentException>(() => new TableSchema(columns));
            Assert.Equal(columns.Count, new TableSchema(columns, new TableBlockLimits(maxColumnsPerTable: columns.Count)).Columns.Count);

            Assert.Single(Schema(("t", T.Time)).Columns);
            Assert.Equal(255, Schema(("time", T.Time), (new string('n', 255), T.Double)).CanonicalBytes[9]);
        }

        [Fact]
        public void ReadDirectory_returns_every_entry_without_the_columns()
        {
            var tables = new[]
            {
                new TestTable("a", 1, new[] { Samples.T0, Samples.T0 + 1 }, new[] { 10L, 11L }, new[] { 0.5, -0.0 }),
                new TestTable("bb", 2, new[] { Samples.T0 + 2 }),
                new TestTable("ccc", 3, new[] { Samples.T0 + 5, Samples.T0 + 3, Samples.T0 + 4 }, new[] { 30L, 31L, 32L }),
            };
            var block = Samples.Write(tables);

            var directory = TableBlockReader.ReadDirectory(block);
            var read = TableBlockReader.Read(block);

            Assert.Equal(read.Header.RowCount, directory.Header.RowCount);
            Assert.Equal(read.Header.FirstTime, directory.Header.FirstTime);
            Assert.Equal(read.Header.LastTime, directory.Header.LastTime);
            Assert.Equal(tables.Length, directory.Entries.Count);

            for (int i = 0; i < tables.Length; i++)
            {
                var entry = directory.Entries[i];
                Assert.Equal(tables[i].Id, entry.TableId);
                Assert.Equal(tables[i].SchemaId, entry.SchemaId);
                Assert.Equal(tables[i].Rows, entry.RowCount);
                Assert.Equal(tables[i].Types, entry.ColumnTypes);
            }

            Assert.True(Schema(("t", T.Time), ("i", T.Int64), ("d", T.Double)).HasColumnTypes(directory.Entries[0].ColumnTypes));

            // ReadTable gives the table's position in the directory, which is how the server names a refused table.
            for (int i = 0; i < tables.Length; i++)
            {
                var table = TableBlockReader.ReadTable(block, tables[i].Id, out var index);
                Assert.Equal(i, index);
                Samples.AssertSame(tables[i], table);
            }

            Assert.Null(TableBlockReader.ReadTable(block, Encoding.ASCII.GetBytes("none"), out var missing));
            Assert.Equal(-1, missing);
        }
    }
}
