using System;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// The value of one cell of a table row: a double, a 64-bit integer, or no value (null). A value type of 16 bytes
    /// that converts implicitly from <see cref="double"/>, <see cref="long"/> (and so <see cref="int"/>) and their
    /// nullable forms, so a row is added without allocating: <c>table.AddRow(time, bid, ask)</c>.
    /// <para>
    /// A cell goes into a column of its type. An integer cell is also taken by a double column (converted); a double
    /// cell is refused by an integer column. Every column except the time may hold no value
    /// (<see cref="Null"/>, or a nullable without a value).
    /// </para>
    /// </summary>
    public readonly struct TableCell : IEquatable<TableCell>
    {
        private const byte NoValue = 0;
        private const byte DoubleValue = 1;
        private const byte Int64Value = 2;

        private readonly long _bits;
        private readonly byte _kind;


        private TableCell(long bits, byte kind)
        {
            _bits = bits;
            _kind = kind;
        }


        /// <summary>A cell without a value.</summary>
        public static TableCell Null => default;

        /// <summary>True when the cell holds no value.</summary>
        public bool IsNull => _kind == NoValue;

        /// <summary>True when the cell holds a double.</summary>
        public bool IsDouble => _kind == DoubleValue;

        /// <summary>True when the cell holds a 64-bit integer.</summary>
        public bool IsInt64 => _kind == Int64Value;

        /// <summary>The double the cell holds; an integer cell is converted. Throws for a cell without a value.</summary>
        public double DoubleValueOrThrow =>
            _kind == DoubleValue ? BitConverter.Int64BitsToDouble(_bits)
            : _kind == Int64Value ? _bits
            : throw new InvalidOperationException("The cell holds no value.");

        /// <summary>The integer the cell holds. Throws for a double cell or a cell without a value.</summary>
        public long Int64ValueOrThrow =>
            _kind == Int64Value ? _bits : throw new InvalidOperationException(_kind == NoValue ? "The cell holds no value." : "The cell holds a double.");


        /// <summary>A cell holding <paramref name="value"/>, stored bit for bit (NaN, the infinities and -0.0 too).</summary>
        public static TableCell FromDouble(double value) => new TableCell(BitConverter.DoubleToInt64Bits(value), DoubleValue);

        /// <summary>A cell holding <paramref name="value"/>.</summary>
        public static TableCell FromInt64(long value) => new TableCell(value, Int64Value);


        public static implicit operator TableCell(double value) => FromDouble(value);

        public static implicit operator TableCell(long value) => FromInt64(value);

        public static implicit operator TableCell(double? value) => value.HasValue ? FromDouble(value.Value) : Null;

        public static implicit operator TableCell(long? value) => value.HasValue ? FromInt64(value.Value) : Null;


        // The raw parts, for the collector's buffers.
        internal long Bits => _bits;

        internal byte Kind => _kind;

        internal const byte KindNone = NoValue;
        internal const byte KindDouble = DoubleValue;
        internal const byte KindInt64 = Int64Value;


        public bool Equals(TableCell other) => _kind == other._kind && _bits == other._bits;

        public override bool Equals(object obj) => obj is TableCell other && Equals(other);

        public override int GetHashCode() => unchecked(_bits.GetHashCode() * 31 + _kind);

        public static bool operator ==(TableCell left, TableCell right) => left.Equals(right);

        public static bool operator !=(TableCell left, TableCell right) => !left.Equals(right);

        public override string ToString() =>
            _kind == DoubleValue ? BitConverter.Int64BitsToDouble(_bits).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : _kind == Int64Value ? _bits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
    }
}
