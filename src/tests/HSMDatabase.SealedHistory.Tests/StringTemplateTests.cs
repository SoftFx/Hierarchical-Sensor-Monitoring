using System.Globalization;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>String column mode 2 (templates with numeric slots): restoration, tokenizing, limits, choice, corruption.</summary>
    public class StringTemplateTests(ITestOutputHelper output)
    {
        // ------------------------------------------------------------------ restoration rules, one row each

        [Theory]
        [InlineData(0L, 0, 0, "0")]
        [InlineData(5L, 0, 0, "5")]
        [InlineData(-5L, 0, 0, "-5")]                         // M < 0 writes '-'
        [InlineData(123L, 0, 3, "000123")]                    // Z zeros before the digits
        [InlineData(0L, 0, 2, "000")]
        [InlineData(long.MaxValue, 0, 0, "9223372036854775807")]
        [InlineData(-long.MaxValue, 0, 0, "-9223372036854775807")]
        [InlineData(50L, 2, 0, "0.50")]                       // F > 0: padded to F + 1 digits, '.' before the last F
        [InlineData(-50L, 2, 0, "-0.50")]
        [InlineData(5L, 3, 0, "0.005")]
        [InlineData(0L, 2, 0, "0.00")]
        [InlineData(0L, 1, 0, "0.0")]
        [InlineData(1205L, 2, 0, "12.05")]
        [InlineData(10L, 1, 0, "1.0")]
        [InlineData(5L, 1, 1, "00.5")]                        // Z zeros before the padded integer part
        [InlineData(1L, 1, 2, "000.1")]
        [InlineData(-1L, 1, 2, "-000.1")]                     // never written by the encoder; restored as written
        [InlineData(123456789012345678L, 18, 0, "0.123456789012345678")]
        [InlineData(1L, 24, 0, "0.000000000000000000000001")]  // F above 18 significant digits
        public void A_slot_restores_by_the_rules(long m, int f, int z, string expected)
        {
            var buffer = new char[256];
            int written = StringTemplate.FormatSlot(buffer, m, f, z);

            Assert.Equal(expected, new string(buffer, 0, written));
            Assert.Equal(expected.Length, StringTemplate.SlotLength(m, f, z));
        }

        [Theory]
        [InlineData("", "", "")]
        [InlineData("plain text", "plain text", "")]
        [InlineData("5", "|", "5,0,0")]
        [InlineData("a 12 b", "a | b", "12,0,0")]
        [InlineData("-5", "|", "-5,0,0")]                         // sign folded
        [InlineData("-0", "-|", "0,0,0")]                         // zero has no sign: '-' stays literal
        [InlineData("-0.0", "-|", "0,1,0")]
        [InlineData("-000.1", "-|", "1,1,2")]                     // leading zeros: '-' stays literal
        [InlineData("-0.50", "|", "-50,2,0")]
        [InlineData("007", "|", "7,0,2")]
        [InlineData("000", "|", "0,0,2")]
        [InlineData("00.5", "|", "5,1,1")]
        [InlineData("1.", "|.", "1,0,0")]                         // digits on one side of '.': integer + literal
        [InlineData(".5", ".|", "5,0,0")]
        [InlineData("1..2", "|..|", "1,0,0;2,0,0")]
        [InlineData("1.2.3", "|.|", "12,1,0;3,0,0")]
        [InlineData("10.0.0.1", "|.|", "100,1,0;1,1,0")]
        [InlineData("--3", "-|", "-3,0,0")]
        [InlineData("+5", "+|", "5,0,0")]                         // only '-' folds
        [InlineData("5-3", "||", "5,0,0;-3,0,0")]
        [InlineData("a-b", "a-b", "")]
        [InlineData("1e-5", "|e|", "1,0,0;-5,0,0")]
        [InlineData("1,5", "|,|", "1,0,0;5,0,0")]
        [InlineData("9223372036854775807", "|", "9223372036854775807,0,0")]
        [InlineData("9223372036854775808", "9223372036854775808", "")] // beyond int64: literal
        [InlineData("-9223372036854775808", "-9223372036854775808", "")]
        [InlineData("12345678901234567890", "12345678901234567890", "")]
        [InlineData("123456789012345678.5", "|.|", "123456789012345678,0,0;5,0,0")] // 19 significant digits: two slots
        [InlineData("12345678901234567.8", "|", "123456789012345678,1,0")]         // 18 significant digits: one slot
        [InlineData("0.123456789012345678", "|", "123456789012345678,18,0")]
        [InlineData("١٢٣ １２ ²", "١٢٣ １２ ²", "")] // only ASCII digits form runs
        [InlineData("x٢ 5", "x٢ |", "5,0,0")]
        [InlineData("\U0001F600 1.5 東", "\U0001F600 | 東", "15,1,0")]
        public void A_string_tokenizes_by_the_rules(string input, string template, string slots)
        {
            var (segments, actual) = StringTemplate.Tokenize(input);

            Assert.Equal(template, string.Join("|", segments));
            Assert.Equal(slots, string.Join(";", actual.Select(s => $"{s.M},{s.F},{s.Z}")));
            Assert.Equal(input, StringTemplate.Restore(segments, actual));
        }

        [Fact]
        public void The_caps_leave_runs_literal()
        {
            int cap = StringTemplate.MaxLeadingZeros, fcap = StringTemplate.MaxFractionDigits;

            AssertSlots(new string('0', cap) + "1", (1, 0, cap));                         // Z at its cap
            AssertSlots(new string('0', cap + 1) + "1");                                  // Z above: literal
            AssertSlots("0." + new string('0', fcap - 1) + "1", (1, fcap, 0));            // F at its cap
            AssertSlots("1." + new string('0', fcap) + "1", (1, 0, 0), (1, 0, fcap));     // F above: two slots, the second with Z = 64
            AssertSlots("1." + new string('0', fcap + 1) + "1", (1, 0, 0));               // and the fraction run stays literal when Z is above too

            var many = string.Join(",", Enumerable.Range(0, StringTemplate.MaxSlotsPerTemplate + 3));
            var (segments, slots) = StringTemplate.Tokenize(many);
            Assert.Equal(StringTemplate.MaxSlotsPerTemplate, slots.Length);
            Assert.EndsWith("255,256,257", segments[^1]);
            Assert.Equal(many, StringTemplate.Restore(segments, slots));

            var atLimit = new string('a', StringTemplate.MaxStringChars - 1) + "7";
            Assert.Single(StringTemplate.Tokenize(atLimit).Slots);
            Assert.Empty(StringTemplate.Tokenize(atLimit + "a").Slots);                    // longer than the cap: one literal segment

            static void AssertSlots(string input, params (long M, int F, int Z)[] expected)
            {
                var (segments, slots) = StringTemplate.Tokenize(input);
                Assert.Equal(expected.Select(e => new StringTemplate.Slot(e.M, e.F, e.Z)), slots);
                Assert.Equal(input, StringTemplate.Restore(segments, slots));
            }
        }

        [Fact]
        public void Tokenize_then_restore_is_the_identity_on_every_shape()
        {
            int seed = Seed(18022);
            var random = new Random(seed);

            for (int i = 0; i < 50_000; i++)
            {
                var s = StringShapes.Random(random);
                var (segments, slots) = StringTemplate.Tokenize(s);
                Assert.True(s == StringTemplate.Restore(segments, slots), $"seed {seed}, string #{i}");
                Assert.True(slots.Length <= StringTemplate.MaxSlotsPerTemplate);
                Assert.All(slots, slot =>
                {
                    Assert.NotEqual(long.MinValue, slot.M);
                    Assert.InRange(slot.F, 0, StringTemplate.MaxFractionDigits);
                    Assert.InRange(slot.Z, 0, StringTemplate.MaxLeadingZeros);
                    Assert.False(slot.M < 0 && slot.Z > 0);
                });
            }
        }


        // ------------------------------------------------------------------ the column and the chunk

        /// <summary>
        /// The encoder's contract for strings, stated directly: String sensors and comments built from templates and
        /// numeric shapes; every chunk decodes and every row matches its source, null and empty included.
        /// </summary>
        [Fact]
        public void Whatever_Encode_accepts_Decode_returns_for_template_strings()
        {
            int seed = Seed(18023);
            var random = new Random(seed);
            int templateColumns = 0, dictionaryColumns = 0, constantColumns = 0;

            for (int run = 0; run < 1500; run++)
            {
                int n = random.Next(4) == 0 ? random.Next(1, 10) : random.Next(10, 700);
                var values = StringShapes.Column(random, n);
                var comments = random.Next(3) == 0 ? new string[n] : StringShapes.Column(random, n);
                bool stringSensor = random.Next(4) != 0;

                var rows = new List<HistoryRow>(n);
                for (int i = 0; i < n; i++)
                {
                    BaseValue value = stringSensor
                        ? new StringValue { Value = values[i], Comment = comments[i] }
                        : new IntegerValue { Value = i, Comment = comments[i] };
                    rows.Add(RoundTripTests.Row(i, value));
                }

                foreach (var column in stringSensor ? [values, comments] : new[] { comments })
                {
                    switch (StringColumn.ChooseMode(column))
                    {
                        case StringColumn.ModeTemplate: templateColumns++; break;
                        case StringColumn.ModeDictionary: dictionaryColumns++; break;
                        default: constantColumns++; break;
                    }
                }

                int limit = random.Next(3) switch { 0 => 8, 1 => 64, _ => ChunkCodec.MaxRowsPerChunk };
                var result = ChunkCodec.Encode(rows, limit);
                Assert.True(result.IsSuccess, $"seed {seed}, run {run}: {result.Refusal}");

                var decoded = result.Chunks.SelectMany(c => ChunkCodec.Decode(c, ChunkCodec.MaxRowsPerChunk)).ToList();
                Assert.Equal(n, decoded.Count);
                for (int i = 0; i < n; i++)
                {
                    Assert.True(RowFidelity.FindDifference(rows[i], decoded[i]) is null, $"seed {seed}, run {run}, row {i}");
                    Assert.Equal(comments[i], decoded[i].Value.Comment);
                    if (stringSensor)
                        Assert.Equal(values[i], ((StringValue)decoded[i].Value).Value);
                }
            }

            output.WriteLine($"seed {seed}: template columns {templateColumns}, dictionary {dictionaryColumns}, constant {constantColumns}");
            Assert.True(templateColumns > 500 && dictionaryColumns > 200 && constantColumns > 50,
                $"seed {seed}: template {templateColumns}, dictionary {dictionaryColumns}, constant {constantColumns}");
        }

        [Fact]
        public void Columns_round_trip_through_the_column_reader_in_every_mode()
        {
            int seed = Seed(18024);
            var random = new Random(seed);
            var modes = new HashSet<byte>();

            for (int run = 0; run < 3000; run++)
            {
                var column = StringShapes.Column(random, random.Next(1, 300));
                var sink = new ByteSink();
                StringColumn.Write(sink, column);
                modes.Add(sink.Written[0]);

                var back = new string[column.Length];
                var reader = new ByteReader(sink.Written);
                StringColumn.Read(ref reader, back);

                Assert.Equal(0, reader.Remaining);
                for (int i = 0; i < column.Length; i++)
                    Assert.True(string.Equals(column[i], back[i], StringComparison.Ordinal) && (column[i] is null) == (back[i] is null), $"seed {seed}, run {run}, row {i}");
            }

            Assert.Equal(new byte[] { 0, 1, 2 }, modes.Order());
        }

        [Fact]
        public void Rows_of_a_zero_slot_template_share_one_string_instance()
        {
            string[] column = ["ok", "value 1", "ok", "value 2", "ok"];
            var sink = new ByteSink();
            Assert.True(StringTemplate.TryWrite(sink, column));

            var back = new string[column.Length];
            var reader = new ByteReader(sink.Written);
            StringColumn.Read(ref reader, back);

            Assert.Equal(column, back);
            Assert.Same(back[0], back[2]);
            Assert.Same(back[0], back[4]);
        }


        /// <summary>
        /// A string longer than 65,536 characters, with digits in it, among rows with slots: the encoder keeps the
        /// template mode and stores the long string as one literal segment (a zero-slot template). End to end, for a
        /// String value column and for a Comment column.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_string_above_the_length_limit_rides_along_as_a_literal_template(bool stringSensor)
        {
            var longText = string.Concat(Enumerable.Range(0, 7000).Select(k => $"r{k} ")); // about 48,000 characters
            string[] longStrings =
            [
                longText + longText,                                                         // above the limit, digits inside
                new string('z', StringTemplate.MaxStringChars - 5) + "12.50",               // exactly at the limit: slots
                new string('y', StringTemplate.MaxStringChars - 4) + "12.50",               // one above: literal
            ];
            Assert.True(longStrings[0].Length > StringTemplate.MaxStringChars);
            Assert.Equal(StringTemplate.MaxStringChars, longStrings[1].Length);
            Assert.Equal(StringTemplate.MaxStringChars + 1, longStrings[2].Length);

            var column = Enumerable.Range(0, 300).Select(i => i % 100 == 7 ? longStrings[i / 100] : $"queue {i * 13} load {i % 17}.{i % 10}").ToArray();
            Assert.Equal(StringColumn.ModeTemplate, StringColumn.ChooseMode(column));
            Assert.Single(StringTemplate.Tokenize(longStrings[0]).Segments);
            Assert.Single(StringTemplate.Tokenize(longStrings[2]).Segments);
            Assert.Equal(2, StringTemplate.Tokenize(longStrings[1]).Segments.Length);

            var rows = column.Select((s, i) => RoundTripTests.Row(i, stringSensor
                ? new StringValue { Value = s, Comment = i % 2 == 0 ? null : "c" }
                : new IntegerValue { Value = i, Comment = s })).ToList();

            var decoded = ChunkCodec.Decode(ChunkCodec.Encode(rows).Chunks.Single());

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Null(RowFidelity.FindDifference(rows[i], decoded[i]));
                Assert.Equal(column[i], stringSensor ? ((StringValue)decoded[i].Value).Value : decoded[i].Value.Comment);
            }
        }

        // ------------------------------------------------------------------ the encoder's choice (encoder revision 1)

        public static IEnumerable<object[]> ChoiceCases()
        {
            yield return ["constant", Enumerable.Repeat("same 1", 50).ToArray(), StringColumn.ModeConstant];
            yield return ["all null", new string[50], StringColumn.ModeConstant];
            yield return ["text without digits", Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? "ok" : "fail").ToArray(), StringColumn.ModeDictionary];
            yield return ["few repeated numbered strings", Enumerable.Range(0, 300).Select(i => $"state {i % 3} code {i % 3 * 11 + 5} level {i % 3 * 7}").ToArray(), StringColumn.ModeDictionary];
            yield return ["a number per row", Enumerable.Range(0, 300).Select(i => $"processed {i * 7} items in {i % 50}.{i % 10} s").ToArray(), StringColumn.ModeTemplate];
            yield return ["null and empty with numbers", Enumerable.Range(0, 300).Select(i => i % 3 == 0 ? null : i % 3 == 1 ? "" : $"retry {i}").ToArray(), StringColumn.ModeTemplate];
            yield return ["above the slot value limit", Enumerable.Range(0, 4096).Select(i => string.Join(" ", Enumerable.Range(i, 65))).ToArray(), StringColumn.ModeDictionary];
            yield return ["above the column character limit", Enumerable.Range(0, 2100).Select(i => new string('w', 2000) + i).ToArray(), StringColumn.ModeDictionary];
        }

        [Theory]
        [MemberData(nameof(ChoiceCases))]
        public void The_encoder_chooses_the_mode_by_encoded_size(string name, string[] column, byte expected)
        {
            Assert.True(expected == StringColumn.ChooseMode(column), name);

            var sink = new ByteSink();
            StringColumn.Write(sink, column);
            var back = new string[column.Length];
            var reader = new ByteReader(sink.Written);
            StringColumn.Read(ref reader, back);
            Assert.Equal(column, back);
        }

        [Fact]
        public void The_encoder_writes_the_smaller_encoding_and_the_dictionary_on_a_tie()
        {
            int seed = Seed(18025);
            var random = new Random(seed);

            for (int run = 0; run < 500; run++)
            {
                var column = StringShapes.Column(random, random.Next(2, 200));
                if (column.All(s => s == column[0]))
                    continue;

                var dictionary = new ByteSink();
                StringColumn.WriteDictionary(dictionary, column);
                var template = new ByteSink();
                bool qualifies = StringTemplate.TryWrite(template, column);

                var chosen = new ByteSink();
                StringColumn.Write(chosen, column);

                var expected = qualifies && template.Length < dictionary.Length ? template.Written : dictionary.Written;
                Assert.True(expected.SequenceEqual(chosen.Written), $"seed {seed}, run {run}");
            }
        }


        // ------------------------------------------------------------------ untrusted bytes

        public static IEnumerable<object[]> CorruptColumns()
        {
            yield return ["template count 0", 2, Bytes(s => s.WriteVarUInt(0))];
            yield return ["template count above the rows", 2, Bytes(s => { s.WriteVarUInt(3); Template(s, "a"); Template(s, "b"); Template(s, "c"); Ints(s, 0, 1); })];
            yield return ["more slots than the cap", 1, Bytes(s => { s.WriteVarUInt(1); s.WriteVarUInt(StringTemplate.MaxSlotsPerTemplate + 2); })];
            yield return ["segment longer than the payload", 1, Bytes(s => { s.WriteVarUInt(1); s.WriteVarUInt(1); s.WriteVarUInt(100); s.WriteByte((byte)'a'); })];
            yield return ["segment not UTF-8", 1, Bytes(s => { s.WriteVarUInt(1); s.WriteVarUInt(1); s.WriteVarUInt(1); s.WriteByte(0xFF); Ints(s, 0); })];
            yield return ["template index out of range", 2, Bytes(s => { s.WriteVarUInt(1); Template(s, "a"); Ints(s, 0, 1); })];
            yield return ["negative template index", 1, Bytes(s => { s.WriteVarUInt(1); Template(s, "a"); Ints(s, -1); })];
            yield return ["slot columns missing", 1, Bytes(s => { s.WriteVarUInt(1); Template(s, "a", ""); Ints(s, 0); })];
            yield return ["M is long.MinValue", 2, Slots([long.MinValue, 1], [0, 0], [0, 0])];
            yield return ["F above the cap", 2, Slots([1, 1], [0, StringTemplate.MaxFractionDigits + 1], [0, 0])];
            yield return ["F negative", 2, Slots([1, 1], [-1, 0], [0, 0])];
            yield return ["Z above the cap", 2, Slots([1, 1], [0, 0], [StringTemplate.MaxLeadingZeros + 1, 0])];
            yield return ["Z negative", 2, Slots([1, 1], [0, 0], [0, -1])];
            yield return ["huge Z", 2, Slots([1, 1], [0, 0], [long.MaxValue, 0])];
            yield return ["huge F", 2, Slots([1, 1], [long.MaxValue, 0], [0, 0])];
        }

        [Theory]
        [MemberData(nameof(CorruptColumns))]
        public void A_template_column_that_breaks_a_bound_is_corrupt(string name, int rows, byte[] column)
        {
            var ex = Assert.Throws<ChunkFormatException>(() =>
            {
                var reader = new ByteReader(column);
                StringColumn.Read(ref reader, new string[rows]);
            });

            Assert.True(ex.Error == ChunkFormatError.Corrupt, name);
            CorruptionTests.AssertCleanFailure(ex);
        }

        [Fact]
        public void Slot_values_above_the_limit_are_corrupt_before_any_slot_array_is_allocated()
        {
            const int rows = ChunkCodec.MaxRowsPerChunk;
            var column = Bytes(s =>
            {
                s.WriteVarUInt(1);
                Template(s, Enumerable.Repeat("", StringTemplate.MaxSlotsPerTemplate + 1).ToArray());
                Ints(s, new long[rows]);
            });

            AssertCorruptWithin(column, rows, 2 * 1024 * 1024, "slot values");
        }

        [Fact]
        public void A_string_above_the_length_limit_is_corrupt_before_it_is_built()
        {
            var at = Slots([1], [0], [0], new string('a', StringTemplate.MaxStringChars - 1));
            var reader = new ByteReader(at);
            var back = new string[1];
            StringColumn.Read(ref reader, back);
            Assert.Equal(StringTemplate.MaxStringChars, back[0].Length);

            AssertCorruptWithin(Slots([1], [0], [0], new string('a', StringTemplate.MaxStringChars)), 1, 4 * StringTemplate.MaxStringChars + 64 * 1024, "above");
            AssertCorruptWithin(Slots([1], [StringTemplate.MaxFractionDigits], [StringTemplate.MaxLeadingZeros], new string('a', StringTemplate.MaxStringChars - 100)), 1, 4 * StringTemplate.MaxStringChars + 64 * 1024, "above");
        }

        [Fact]
        public void A_column_above_the_character_limit_is_corrupt_before_any_string_is_built()
        {
            // 4,096 rows of one 2,000-character segment and a slot: a few kilobytes that would expand to 16 MB of strings.
            const int rows = ChunkCodec.MaxRowsPerChunk;
            var column = Bytes(s =>
            {
                s.WriteVarUInt(1);
                Template(s, new string('a', 2000), "");
                Ints(s, new long[rows]);
                Ints(s, Enumerable.Repeat(1L, rows).ToArray());
                Ints(s, new long[rows]);
                Ints(s, new long[rows]);
            });

            Assert.True(column.Length < 3000);
            AssertCorruptWithin(column, rows, 2 * 1024 * 1024, "characters");
        }

        [Fact]
        public void Wrong_slot_value_counts_and_trailing_bytes_are_corrupt_in_a_chunk()
        {
            var rows = Enumerable.Range(0, 3).Select(i => RoundTripTests.Row(i, new StringValue { Value = $"v {i * 1000 + 17}" })).ToList();

            byte[] shortColumn = Bytes(s => { s.WriteVarUInt(1); Template(s, "v ", ""); Ints(s, 0, 0, 0); Varints(s, 17, 1017); Ints(s, 0, 0, 0); Ints(s, 0, 0, 0); });
            byte[] longColumn = Bytes(s => { s.WriteVarUInt(1); Template(s, "v ", ""); Ints(s, 0, 0, 0); Varints(s, 17, 1017, 2017, 3017); Ints(s, 0, 0, 0); Ints(s, 0, 0, 0); });
            byte[] good = Bytes(s => { s.WriteVarUInt(1); Template(s, "v ", ""); Ints(s, 0, 0, 0); Varints(s, 17, 1017, 2017); Ints(s, 0, 0, 0); Ints(s, 0, 0, 0); });

            Assert.Equal(rows.Select(r => ((StringValue)r.Value).Value), ChunkCodec.Decode(ChunkWithValueColumn(rows, good)).Select(r => ((StringValue)r.Value).Value));

            foreach (var column in new[] { shortColumn, longColumn, good.Concat(new byte[] { 0 }).ToArray(), good.AsSpan(0, good.Length - 1).ToArray() })
            {
                var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(ChunkWithValueColumn(rows, column)));
                Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
                CorruptionTests.AssertCleanFailure(ex);
            }
        }

        /// <summary>
        /// Payload damage behind a valid checksum, on chunks whose string columns use templates: the parser itself
        /// is exercised. Every decode returns all rows or throws Corrupt, and none allocates beyond the bounds. (A
        /// damaged digit can decode to another valid string: the checksum, not the parser, catches that.)
        /// </summary>
        [Fact]
        public void Fuzzed_template_payloads_fail_cleanly_and_within_the_allocation_bounds()
        {
            int seed = Seed(18026);
            var random = new Random(seed);
            long maxAllocated = 0;
            int decodedFine = 0, corrupt = 0;

            foreach (var rows in TemplateChunks(random, 6))
            {
                var good = ChunkCodec.Encode(rows).Chunks.Single();
                var payload = ChunkCodec.ExtractPayload(good);
                var header = ChunkCodec.ReadHeader(good);

                for (int iteration = 0; iteration < 1500; iteration++)
                {
                    var damaged = Mutate(payload, random);
                    var chunk = ChunkCodec.Assemble(damaged, header.ValueType, header.RowCount, header.FirstKey, header.LastKey, allowCompression: iteration % 8 == 0);

                    long before = GC.GetAllocatedBytesForCurrentThread();
                    try
                    {
                        var decoded = ChunkCodec.Decode(chunk);
                        Assert.Equal(header.RowCount, decoded.Count);
                        decodedFine++;
                    }
                    catch (ChunkFormatException ex)
                    {
                        CorruptionTests.AssertCleanFailure(ex);
                        corrupt++;
                    }
                    maxAllocated = Math.Max(maxAllocated, GC.GetAllocatedBytesForCurrentThread() - before);
                }
            }

            output.WriteLine($"seed {seed}: decoded {decodedFine}, corrupt {corrupt}, largest allocation of one decode {maxAllocated:N0} B");
            Assert.True(maxAllocated < 32L * 1024 * 1024, $"seed {seed}: one decode allocated {maxAllocated:N0} B");
            Assert.True(corrupt > 1000, $"seed {seed}: only {corrupt} corrupt");
        }

        [Fact]
        public void Every_bit_flip_and_truncation_of_a_template_chunk_is_detected()
        {
            var rows = TemplateChunks(new Random(18027), 1).Single();
            var chunk = ChunkCodec.Encode(rows).Chunks.Single();
            Assert.Equal(StringColumn.ModeTemplate, StringColumn.ChooseMode(rows.Select(r => ((StringValue)r.Value).Value).ToArray()));

            for (int bit = 0; bit < chunk.Length * 8; bit++)
            {
                var damaged = (byte[])chunk.Clone();
                damaged[bit / 8] ^= (byte)(1 << (bit % 8));
                CorruptionTests.AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(damaged)));
            }

            for (int length = 0; length < chunk.Length; length++)
                Assert.Equal(ChunkFormatError.Truncated, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk.AsSpan(0, length))).Error);
        }

        [Fact]
        public void Bit_flips_with_the_checksum_recomputed_never_crash_the_template_parser()
        {
            int seed = Seed(18028);
            var random = new Random(seed);
            var rows = TemplateChunks(random, 1).Single();
            var payload = ChunkCodec.ExtractPayload(ChunkCodec.Encode(rows).Chunks.Single());
            int changed = 0;

            for (int bit = 0; bit < payload.Length * 8; bit += 3)
            {
                var damaged = (byte[])payload.Clone();
                damaged[bit / 8] ^= (byte)(1 << (bit % 8));
                var chunk = ChunkCodec.Assemble(damaged, SensorType.String, rows.Count, rows[0].Key, rows[^1].Key, allowCompression: false);

                try
                {
                    var decoded = ChunkCodec.Decode(chunk);
                    Assert.Equal(rows.Count, decoded.Count);
                    if (Enumerable.Range(0, rows.Count).Any(i => RowFidelity.FindDifference(rows[i], decoded[i]) is not null))
                        changed++;
                }
                catch (ChunkFormatException ex)
                {
                    CorruptionTests.AssertCleanFailure(ex);
                }
            }

            output.WriteLine($"seed {seed}: {changed} flips decoded to different rows (the checksum is what catches those)");
        }


        // ------------------------------------------------------------------ helpers

        private int Seed(int fallback)
        {
            int seed = int.TryParse(Environment.GetEnvironmentVariable("HSM_STRING_TEST_SEED"), out var s) ? s : fallback;
            output.WriteLine($"seed {seed} (set HSM_STRING_TEST_SEED to replay another)");
            return seed;
        }

        private static IEnumerable<List<HistoryRow>> TemplateChunks(Random random, int count)
        {
            for (int c = 0; c < count; c++)
            {
                List<HistoryRow> rows;
                string[] values;
                do
                {
                    int n = random.Next(100, 400);
                    values = Enumerable.Range(0, n).Select(i => random.Next(5) == 0 ? StringShapes.Random(random) : string.Create(CultureInfo.InvariantCulture,
                        $"queue {random.Next(500)} load {random.Next(10_000) / 100.0:F2} at {i % 24:D2}:{random.Next(60):D2}")).ToArray();
                    var comments = Enumerable.Range(0, n).Select(i => i % 3 == 0 ? null : $"restart {i} after -{random.Next(100)}.{random.Next(10)} s").ToArray();
                    rows = Enumerable.Range(0, n).Select(i => RoundTripTests.Row(i, new StringValue { Value = values[i], Comment = comments[i] })).ToList();
                }
                while (StringColumn.ChooseMode(values) != StringColumn.ModeTemplate);

                yield return rows;
            }
        }

        private static byte[] ChunkWithValueColumn(List<HistoryRow> rows, byte[] column)
        {
            var full = new ByteSink();
            PayloadWriter.Write(full, rows, 0, rows.Count, SensorType.String);
            var valueOnly = new ByteSink();
            StringColumn.Write(valueOnly, rows.Select(r => ((StringValue)r.Value).Value).ToArray());

            var payload = full.Written.ToArray();
            Assert.True(payload.AsSpan(payload.Length - valueOnly.Length).SequenceEqual(valueOnly.Written), "the String value column is not the last column");

            byte[] spliced = [.. payload.AsSpan(0, payload.Length - valueOnly.Length), .. column];
            return ChunkCodec.Assemble(spliced, SensorType.String, rows.Count, rows[0].Key, rows[^1].Key);
        }

        private static void AssertCorruptWithin(byte[] column, int rows, long maxAllocated, string message)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var ex = Assert.Throws<ChunkFormatException>(() =>
            {
                var reader = new ByteReader(column);
                StringColumn.Read(ref reader, new string[rows]);
            });
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
            Assert.Contains(message, ex.Message);
            Assert.True(allocated < maxAllocated, $"allocated {allocated:N0} B before failing");
        }

        /// <summary>A mode-2 column: one template "segment + slot + empty", the given M, F, Z columns.</summary>
        private static byte[] Slots(long[] m, long[] f, long[] z, string segment = "v") => Bytes(s =>
        {
            s.WriteVarUInt(1);
            Template(s, segment, "");
            Ints(s, new long[m.Length]);
            Ints(s, m);
            Ints(s, f);
            Ints(s, z);
        });

        private static byte[] Bytes(Action<ByteSink> body)
        {
            var sink = new ByteSink();
            sink.WriteByte(StringColumn.ModeTemplate);
            body(sink);
            return sink.Written.ToArray();
        }

        private static void Template(ByteSink sink, params string[] segments)
        {
            sink.WriteVarUInt((ulong)segments.Length);
            foreach (var segment in segments)
            {
                var bytes = StringColumn.StrictUtf8.GetBytes(segment);
                sink.WriteVarUInt((ulong)bytes.Length);
                sink.WriteBytes(bytes);
            }
        }

        private static void Ints(ByteSink sink, params long[] values) => IntColumn.Write(sink, values);

        /// <summary>An integer column in varint mode, raw order, whatever its values: its count is implicit.</summary>
        private static void Varints(ByteSink sink, params long[] values)
        {
            sink.WriteByte(IntColumn.ModeVarint);
            sink.WriteByte(IntColumn.OrderRaw);
            foreach (var v in values)
                sink.WriteVarInt(v);
        }

        private static byte[] Mutate(byte[] payload, Random random)
        {
            var bytes = new List<byte>(payload);
            int edits = random.Next(1, 4);

            for (int e = 0; e < edits && bytes.Count > 1; e++)
            {
                switch (random.Next(5))
                {
                    case 0: bytes[random.Next(bytes.Count)] ^= (byte)(1 << random.Next(8)); break;
                    case 1: bytes[random.Next(bytes.Count)] = (byte)random.Next(256); break;
                    case 2: bytes.RemoveAt(random.Next(bytes.Count)); break;
                    case 3: bytes.Insert(random.Next(bytes.Count + 1), (byte)random.Next(256)); break;
                    default: bytes[random.Next(bytes.Count)] = 0xFF; break;
                }
            }

            return bytes.ToArray();
        }
    }
}
