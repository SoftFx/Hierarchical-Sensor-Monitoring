using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// String column mode 2, "template": every string is split into literal segments around numeric slots. A slot
    /// is an ASCII digit run, optionally with a '.' and a fraction run, optionally with a folded '-', stored as three
    /// integers: a signed mantissa M, a fraction-digit count F and an extra-leading-zero count Z. The literal
    /// segments form a template; templates are kept in a dictionary per column, and each slot of each template is an
    /// integer column over the rows that use the template. The byte layout, the tokenizing rules and the bounds are
    /// specified in <c>aicontext/features/storage/sealed-history/feature.md</c>.
    /// </summary>
    internal static class StringTemplate
    {
        /// <summary>The most slots one template (one string) may have; runs after the last slot stay literal.</summary>
        public const int MaxSlotsPerTemplate = 255;

        /// <summary>The largest fraction-digit count F; a longer fraction is not a decimal slot.</summary>
        public const int MaxFractionDigits = 64;

        /// <summary>The largest extra-leading-zero count Z; a run with more leading zeros is not a slot.</summary>
        public const int MaxLeadingZeros = 64;

        /// <summary>The most significant digits of a decimal slot, so that its mantissa fits in an int64.</summary>
        public const int MaxDecimalDigits = 18;

        /// <summary>
        /// The longest string a template with slots may produce, in UTF-16 code units. A longer string is
        /// tokenized as one literal segment (a zero-slot template, stored once and shared like a dictionary entry).
        /// </summary>
        public const int MaxStringChars = 65_536;

        /// <summary>
        /// The most UTF-16 code units the rows of a column that use templates with slots may produce together. Those
        /// strings are built per row, so this bounds what a decoder allocates beyond the payload's own bytes. A column
        /// above it is written as a dictionary.
        /// </summary>
        public const int MaxColumnChars = 4 * 1024 * 1024;

        /// <summary>The most slot values (rows times slots, summed over templates) one column may hold.</summary>
        public const int MaxSlotValues = 262_144;


        internal readonly record struct Slot(long M, int F, int Z);


        /// <summary>
        /// Writes the column in mode 2 and returns true, or writes nothing and returns false when the column does not
        /// qualify: no string has a slot, or the column would pass <see cref="MaxSlotValues"/> or
        /// <see cref="MaxColumnChars"/>. Null rows use a null template.
        /// </summary>
        public static bool TryWrite(ByteSink sink, ReadOnlySpan<string> values)
        {
            var templates = new List<string[]>();
            var templateOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var tokensOf = new Dictionary<string, (int Template, Slot[] Slots)>(StringComparer.Ordinal);
            var slotsByTemplate = new List<List<Slot[]>>();
            var indices = new long[values.Length];
            int nullTemplate = -1;
            long slotValues = 0, columnChars = 0;
            bool anySlot = false;

            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];

                if (value is null)
                {
                    if (nullTemplate < 0)
                    {
                        nullTemplate = templates.Count;
                        templates.Add(null);
                        slotsByTemplate.Add(null);
                    }
                    indices[i] = nullTemplate;
                    continue;
                }

                if (!tokensOf.TryGetValue(value, out var tokens))
                {
                    var (segments, slots) = Tokenize(value);
                    var key = Key(segments);

                    if (!templateOf.TryGetValue(key, out int template))
                    {
                        template = templates.Count;
                        templates.Add(segments);
                        slotsByTemplate.Add(new List<Slot[]>());
                        templateOf.Add(key, template);
                    }

                    tokens = (template, slots);
                    tokensOf.Add(value, tokens);
                }

                indices[i] = tokens.Template;

                if (tokens.Slots.Length > 0)
                {
                    anySlot = true;
                    slotValues += tokens.Slots.Length;
                    columnChars += value.Length;
                    if (slotValues > MaxSlotValues || columnChars > MaxColumnChars)
                        return false;
                }

                slotsByTemplate[tokens.Template]?.Add(tokens.Slots);
            }

            if (!anySlot)
                return false;

            sink.WriteByte(StringColumn.ModeTemplate);
            sink.WriteVarUInt((ulong)templates.Count);

            foreach (var segments in templates)
            {
                if (segments is null)
                {
                    sink.WriteVarUInt(0);
                    continue;
                }

                sink.WriteVarUInt((ulong)segments.Length);
                foreach (var segment in segments)
                {
                    // UTF-8 takes at least one byte per UTF-16 code unit: check before GetByteCount (no int overflow).
                    sink.Ensure(segment.Length);
                    var bytes = StringColumn.StrictUtf8.GetBytes(segment);
                    sink.WriteVarUInt((ulong)bytes.Length);
                    sink.WriteBytes(bytes);
                }
            }

            IntColumn.Write(sink, indices);

            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t] is null || templates[t].Length == 1)
                    continue;

                var rows = slotsByTemplate[t];
                var column = new long[rows.Count];

                for (int s = 0; s < templates[t].Length - 1; s++)
                {
                    for (int r = 0; r < rows.Count; r++)
                        column[r] = rows[r][s].M;
                    IntColumn.Write(sink, column);

                    for (int r = 0; r < rows.Count; r++)
                        column[r] = rows[r][s].F;
                    IntColumn.Write(sink, column);

                    for (int r = 0; r < rows.Count; r++)
                        column[r] = rows[r][s].Z;
                    IntColumn.Write(sink, column);
                }
            }

            return true;
        }

        public static void Read(ref ByteReader reader, Span<string> destination)
        {
            int n = destination.Length;

            if (reader.ReadByte() != StringColumn.ModeTemplate)
                throw Corrupt.Error("not a template string column");

            int count = reader.ReadCount(n);
            if (count == 0)
                throw Corrupt.Error("empty template dictionary");

            var templates = new string[count][];
            var literalChars = new long[count];

            for (int t = 0; t < count; t++)
            {
                ulong header = reader.ReadVarUInt();
                if (header == 0)
                    continue;

                if (header > MaxSlotsPerTemplate + 1)
                    throw Corrupt.Error($"template with {header} segments, above {MaxSlotsPerTemplate + 1}");

                var segments = new string[(int)header];
                for (int s = 0; s < segments.Length; s++)
                {
                    int length = reader.ReadCount(reader.Remaining);
                    segments[s] = DecodeUtf8(reader.ReadBytes(length));
                    literalChars[t] += segments[s].Length;
                }

                templates[t] = segments;
            }

            var indices = new long[n];
            IntColumn.Read(ref reader, indices);

            var uses = new int[count];
            foreach (var index in indices)
            {
                if ((ulong)index >= (ulong)count)
                    throw Corrupt.Error($"template index {index} out of range");
                uses[index]++;
            }

            // Bound the slot arrays before allocating any of them.
            long slotValues = 0;
            for (int t = 0; t < count; t++)
                if (templates[t] is not null && uses[t] > 0)
                    slotValues += (long)uses[t] * (templates[t].Length - 1);

            if (slotValues > MaxSlotValues)
                throw Corrupt.Error($"{slotValues} slot values, above {MaxSlotValues}");

            // Per template and slot: M, F, Z over the rows using the template, in row order.
            var slots = new long[count][][];
            for (int t = 0; t < count; t++)
            {
                if (templates[t] is null || templates[t].Length == 1 || uses[t] == 0)
                    continue;

                slots[t] = new long[(templates[t].Length - 1) * 3][];
                for (int c = 0; c < slots[t].Length; c++)
                {
                    var column = new long[uses[t]];
                    IntColumn.Read(ref reader, column);
                    CheckSlotColumn(column, c % 3);
                    slots[t][c] = column;
                }
            }

            // Every row's length, checked against the limits before any string is built.
            var lengths = new int[n];
            var cursor = new int[count];
            long columnChars = 0;

            for (int i = 0; i < n; i++)
            {
                int t = (int)indices[i];
                if (templates[t] is null || templates[t].Length == 1)
                    continue;

                int row = cursor[t]++;
                long length = literalChars[t];
                for (int s = 0; s < templates[t].Length - 1; s++)
                    length += SlotLength(slots[t][s * 3][row], slots[t][s * 3 + 1][row], slots[t][s * 3 + 2][row]);

                if (length > MaxStringChars)
                    throw Corrupt.Error($"a template string of {length} characters, above {MaxStringChars}");

                columnChars += length;
                if (columnChars > MaxColumnChars)
                    throw Corrupt.Error($"template strings of more than {MaxColumnChars} characters in one column");

                lengths[i] = (int)length;
            }

            Array.Clear(cursor);

            for (int i = 0; i < n; i++)
            {
                int t = (int)indices[i];
                var segments = templates[t];

                if (segments is null)
                {
                    destination[i] = null;
                    continue;
                }

                if (segments.Length == 1)
                {
                    destination[i] = segments[0]; // one shared instance for every row of the template
                    continue;
                }

                int row = cursor[t]++;
                destination[i] = string.Create(lengths[i], (segments, slots[t], row), static (span, state) =>
                {
                    var (segs, columns, r) = state;
                    int position = 0;

                    segs[0].AsSpan().CopyTo(span);
                    position += segs[0].Length;

                    for (int s = 1; s < segs.Length; s++)
                    {
                        position += FormatSlot(span.Slice(position), columns[(s - 1) * 3][r], (int)columns[(s - 1) * 3 + 1][r], (int)columns[(s - 1) * 3 + 2][r]);
                        segs[s].AsSpan().CopyTo(span.Slice(position));
                        position += segs[s].Length;
                    }
                });
            }
        }


        // ------------------------------------------------------------------ tokenizing (encoder revision 1)

        /// <summary>
        /// Splits a string into literal segments (slot count + 1 of them) and slots. Only ASCII '0'-'9' form runs;
        /// every other character, any other Unicode digit, sign or separator included, is literal. See the format
        /// specification for the rules.
        /// </summary>
        public static (string[] Segments, Slot[] Slots) Tokenize(string value)
        {
            if (value.Length > MaxStringChars)
                return ([value], []);

            var segments = new List<string>();
            var slots = new List<Slot>();
            var literal = new StringBuilder();
            int i = 0;

            while (i < value.Length)
            {
                if (!char.IsAsciiDigit(value[i]))
                {
                    literal.Append(value[i++]);
                    continue;
                }

                int start = i;
                while (i < value.Length && char.IsAsciiDigit(value[i]))
                    i++;
                var run = value.AsSpan(start, i - start);

                if (slots.Count < MaxSlotsPerTemplate)
                {
                    if (i + 1 < value.Length && value[i] == '.' && char.IsAsciiDigit(value[i + 1]))
                    {
                        int end = i + 1;
                        while (end < value.Length && char.IsAsciiDigit(value[end]))
                            end++;

                        if (TryDecimal(run, value.AsSpan(i + 1, end - i - 1), out long m, out int z))
                        {
                            Emit(m, end - i - 1, z);
                            i = end;
                            continue;
                        }
                    }

                    if (TryInteger(run, out long integer, out int zeros))
                    {
                        Emit(integer, 0, zeros);
                        continue;
                    }
                }

                literal.Append(run);
            }

            segments.Add(literal.ToString());
            return (segments.ToArray(), slots.ToArray());

            void Emit(long m, int f, int z)
            {
                // A '-' right before the run folds into the mantissa, unless it could not be restored from it:
                // with leading zeros the '-' would print before them, and zero has no sign.
                if (z == 0 && m != 0 && literal.Length > 0 && literal[^1] == '-')
                {
                    literal.Length--;
                    m = -m;
                }

                segments.Add(literal.ToString());
                literal.Clear();
                slots.Add(new Slot(m, f, z));
            }
        }

        /// <summary>An integer run: leading zeros (all but the last digit) in Z, the rest an int64.</summary>
        private static bool TryInteger(ReadOnlySpan<char> run, out long value, out int zeros)
        {
            value = 0;
            zeros = LeadingZeros(run);
            var rest = run.Slice(zeros);

            return zeros <= MaxLeadingZeros && rest.Length <= 19 &&
                   long.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// A decimal <c>a.b</c>: the integer part's leading zeros (all but its last digit) in Z, F = |b|, and the
        /// mantissa is the value of the remaining integer digits followed by b, at most 18 significant digits.
        /// </summary>
        private static bool TryDecimal(ReadOnlySpan<char> a, ReadOnlySpan<char> b, out long mantissa, out int zeros)
        {
            mantissa = 0;
            zeros = LeadingZeros(a);
            var natural = a.Slice(zeros);

            if (zeros > MaxLeadingZeros || b.Length > MaxFractionDigits)
                return false;

            if (natural is "0")
            {
                var significant = b.TrimStart('0');
                return significant.Length <= MaxDecimalDigits &&
                       (significant.Length == 0 || long.TryParse(significant, NumberStyles.None, CultureInfo.InvariantCulture, out mantissa));
            }

            if (natural.Length + b.Length > MaxDecimalDigits)
                return false;

            long integerPart = long.Parse(natural, NumberStyles.None, CultureInfo.InvariantCulture);
            long fraction = long.Parse(b, NumberStyles.None, CultureInfo.InvariantCulture);
            mantissa = integerPart * Pow10[b.Length] + fraction;
            return true;
        }

        /// <summary>Leading '0's of a run, leaving at least its last digit.</summary>
        private static int LeadingZeros(ReadOnlySpan<char> run)
        {
            int zeros = 0;
            while (zeros < run.Length - 1 && run[zeros] == '0')
                zeros++;
            return zeros;
        }

        private static readonly long[] Pow10 = BuildPow10();

        private static long[] BuildPow10()
        {
            var powers = new long[MaxDecimalDigits + 1];
            powers[0] = 1;
            for (int i = 1; i < powers.Length; i++)
                powers[i] = powers[i - 1] * 10;
            return powers;
        }


        // ------------------------------------------------------------------ restoring

        /// <summary>
        /// The number of characters a slot restores to. The arguments are already range-checked: M is not
        /// <see cref="long.MinValue"/>, F and Z are within their caps.
        /// </summary>
        public static long SlotLength(long m, long f, long z)
        {
            int digits = DigitCount(m < 0 ? (ulong)-m : (ulong)m);
            long body = f == 0 ? digits : Math.Max(digits, f + 1) + 1;
            return (m < 0 ? 1 : 0) + z + body;
        }

        /// <summary>
        /// Writes a slot: '-' if M &lt; 0; Z zeros; |M| in decimal digits; with F &gt; 0 the digits are left-padded with
        /// zeros to F + 1 and a '.' goes before the last F of them. Returns the characters written.
        /// </summary>
        public static int FormatSlot(Span<char> destination, long m, int f, int z)
        {
            int position = 0;

            if (m < 0)
                destination[position++] = '-';

            destination.Slice(position, z).Fill('0');
            position += z;

            Span<char> digits = stackalloc char[20];
            ((ulong)(m < 0 ? -m : m)).TryFormat(digits, out int length, default, CultureInfo.InvariantCulture);

            if (f == 0)
            {
                digits.Slice(0, length).CopyTo(destination.Slice(position));
                return position + length;
            }

            int width = Math.Max(length, f + 1);
            int padding = width - length;
            int integerDigits = width - f;

            // The padded digit string is padding zeros followed by the digits; split it after integerDigits.
            for (int k = 0; k < width; k++)
            {
                if (k == integerDigits)
                    destination[position++] = '.';
                destination[position++] = k < padding ? '0' : digits[k - padding];
            }

            return position;
        }

        /// <summary>A string from its segments and slots (the inverse of <see cref="Tokenize"/>).</summary>
        public static string Restore(string[] segments, Slot[] slots)
        {
            var builder = new StringBuilder(segments[0]);
            Span<char> buffer = stackalloc char[2 * MaxLeadingZeros + 2 * MaxFractionDigits + 64];

            for (int s = 0; s < slots.Length; s++)
            {
                int written = FormatSlot(buffer, slots[s].M, slots[s].F, slots[s].Z);
                builder.Append(buffer.Slice(0, written));
                builder.Append(segments[s + 1]);
            }

            return builder.ToString();
        }


        private static void CheckSlotColumn(long[] column, int kind)
        {
            foreach (var v in column)
            {
                switch (kind)
                {
                    case 0 when v == long.MinValue:
                        throw Corrupt.Error("slot mantissa is long.MinValue");
                    case 1 when v < 0 || v > MaxFractionDigits:
                        throw Corrupt.Error($"slot fraction-digit count {v} outside 0..{MaxFractionDigits}");
                    case 2 when v < 0 || v > MaxLeadingZeros:
                        throw Corrupt.Error($"slot leading-zero count {v} outside 0..{MaxLeadingZeros}");
                }
            }
        }

        private static int DigitCount(ulong value)
        {
            int digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }
            return digits;
        }

        private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
        {
            try
            {
                return StringColumn.StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ChunkFormatException(ChunkFormatError.Corrupt, "template segment is not valid UTF-8", ex);
            }
        }

        /// <summary>An unambiguous key for a segment list: every segment prefixed by its length.</summary>
        private static string Key(string[] segments)
        {
            var builder = new StringBuilder();
            foreach (var segment in segments)
                builder.Append(segment.Length).Append(':').Append(segment);
            return builder.ToString();
        }
    }
}
