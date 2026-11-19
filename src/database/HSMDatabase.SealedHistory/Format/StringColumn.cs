using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// A column of n &gt;= 1 nullable strings. Mode 0 = one entry for the whole column; mode 1 = a dictionary
    /// of the distinct entries in order of first appearance, then an integer column of entry indices.
    /// An entry is a varint (0 = null, otherwise UTF-8 byte length + 1) followed by the UTF-8 bytes, so null
    /// and the empty string stay distinct.
    /// </summary>
    internal static class StringColumn
    {
        internal const byte ModeConstant = 0;
        internal const byte ModeDictionary = 1;

        internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);


        /// <summary>True when the string can be stored as UTF-8 and read back to the same UTF-16 code units.</summary>
        public static bool IsRepresentable(string value)
        {
            if (value is null)
                return true;

            // A scan instead of GetByteCount: no exception path and no int overflow on very long strings.
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                        return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                    return false;
            }

            return true;
        }

        public static void Write(ByteSink sink, ReadOnlySpan<string> values)
        {
            if (values.Length == 0)
                throw new InvalidOperationException("A string column holds at least one value.");

            bool constant = true;
            for (int i = 1; i < values.Length && constant; i++)
                constant = string.Equals(values[i], values[0], StringComparison.Ordinal);

            if (constant)
            {
                sink.WriteByte(ModeConstant);
                WriteEntry(sink, values[0]);
                return;
            }

            var dictionary = new List<string>();
            var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            int nullIndex = -1;
            var indices = new long[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                int index;

                if (value is null)
                {
                    if (nullIndex < 0)
                    {
                        nullIndex = dictionary.Count;
                        dictionary.Add(null);
                    }
                    index = nullIndex;
                }
                else if (!lookup.TryGetValue(value, out index))
                {
                    index = dictionary.Count;
                    dictionary.Add(value);
                    lookup.Add(value, index);
                }

                indices[i] = index;
            }

            sink.WriteByte(ModeDictionary);
            sink.WriteVarUInt((ulong)dictionary.Count);
            foreach (var entry in dictionary)
                WriteEntry(sink, entry);

            IntColumn.Write(sink, indices);
        }

        public static void Read(ref ByteReader reader, Span<string> destination)
        {
            if (destination.Length == 0)
                throw new InvalidOperationException("A string column holds at least one value.");

            byte mode = reader.ReadByte();

            if (mode == ModeConstant)
            {
                destination.Fill(ReadEntry(ref reader));
                return;
            }

            if (mode != ModeDictionary)
                throw Corrupt.Error($"unknown string column mode {mode}");

            int count = reader.ReadCount(destination.Length);
            if (count == 0)
                throw Corrupt.Error("empty string dictionary");

            var dictionary = new string[count];
            for (int i = 0; i < count; i++)
                dictionary[i] = ReadEntry(ref reader);

            var indices = new long[destination.Length];
            IntColumn.Read(ref reader, indices);

            for (int i = 0; i < destination.Length; i++)
            {
                long index = indices[i];
                if ((ulong)index >= (ulong)count)
                    throw Corrupt.Error($"string dictionary index {index} out of range");

                destination[i] = dictionary[index];
            }
        }


        private static void WriteEntry(ByteSink sink, string value)
        {
            if (value is null)
            {
                sink.WriteVarUInt(0);
                return;
            }

            // UTF-8 takes at least one byte per UTF-16 code unit: a longer string cannot fit, and checking first
            // keeps GetByteCount away from int overflow.
            sink.Ensure(value.Length);

            int length = StrictUtf8.GetByteCount(value);
            sink.WriteVarUInt((ulong)length + 1);
            sink.Ensure(length);
            sink.WriteBytes(StrictUtf8.GetBytes(value));
        }

        private static string ReadEntry(ref ByteReader reader)
        {
            ulong header = reader.ReadVarUInt();
            if (header == 0)
                return null;

            if (header - 1 > (ulong)reader.Remaining)
                throw Corrupt.Error("string entry runs past the end of the payload");

            var bytes = reader.ReadBytes((int)(header - 1));

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ChunkFormatException(ChunkFormatError.Corrupt, "string entry is not valid UTF-8", ex);
            }
        }
    }


    /// <summary>
    /// Which rows of a nullable column hold a value. Mode 0 = all rows, mode 1 = no row, mode 2 = a bitmap of
    /// ceil(n / 8) bytes, row i in bit (i mod 8) of byte (i / 8), least significant bit first, unused bits zero.
    /// </summary>
    internal static class Presence
    {
        internal const byte ModeAll = 0;
        internal const byte ModeNone = 1;
        internal const byte ModeBitmap = 2;


        public static void Write(ByteSink sink, ReadOnlySpan<bool> present)
        {
            int count = 0;
            foreach (var p in present)
                if (p)
                    count++;

            if (count == present.Length)
            {
                sink.WriteByte(ModeAll);
                return;
            }

            if (count == 0)
            {
                sink.WriteByte(ModeNone);
                return;
            }

            sink.WriteByte(ModeBitmap);
            for (int i = 0; i < present.Length; i += 8)
            {
                byte b = 0;
                for (int j = 0; j < 8 && i + j < present.Length; j++)
                    if (present[i + j])
                        b |= (byte)(1 << j);

                sink.WriteByte(b);
            }
        }

        /// <summary>Fills <paramref name="present"/> and returns the number of rows that hold a value.</summary>
        public static int Read(ref ByteReader reader, Span<bool> present)
        {
            byte mode = reader.ReadByte();

            switch (mode)
            {
                case ModeAll:
                    present.Fill(true);
                    return present.Length;

                case ModeNone:
                    present.Fill(false);
                    return 0;

                case ModeBitmap:
                    {
                        var bytes = reader.ReadBytes((present.Length + 7) / 8);
                        int count = 0;

                        for (int i = 0; i < present.Length; i++)
                        {
                            bool p = (bytes[i >> 3] & (1 << (i & 7))) != 0;
                            present[i] = p;
                            if (p)
                                count++;
                        }

                        int usedBits = present.Length & 7;
                        if (usedBits != 0 && (bytes[^1] >> usedBits) != 0)
                            throw Corrupt.Error("presence bitmap padding is not zero");

                        return count;
                    }

                default:
                    throw Corrupt.Error($"unknown presence mode {mode}");
            }
        }
    }
}
