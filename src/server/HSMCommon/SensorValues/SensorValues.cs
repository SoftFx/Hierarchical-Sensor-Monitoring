using System;
using System.IO;
using HSMCommon.Extensions;
using MemoryPack;


namespace HSMCommon.Model
{
    [MemoryPackable]
    public partial record BooleanValue : BaseValue<bool>
    {
        public override SensorType Type => SensorType.Boolean;


        public override bool TryParseValue(string value, out bool parsedValue) => bool.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record IntegerValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Integer;


        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record DoubleValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Double;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record StringValue : BaseValue<string>
    {
        public override SensorType Type => SensorType.String;


        public override bool TryParseValue(string value, out string parsedValue)
        {
            parsedValue = value;

            return !string.IsNullOrWhiteSpace(value);
        }
    }

    [MemoryPackable]
    public partial record TimeSpanValue : BaseValue<TimeSpan>
    {

        public override SensorType Type => SensorType.TimeSpan;

        public override object RawValue => Value.Ticks;


        public override bool TryParseValue(string value, out TimeSpan parsedValue) => TimeSpan.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record VersionValue : BaseValue<Version>
    {
        public override SensorType Type => SensorType.Version;

        public override string ShortInfo => Value?.Revision == 0 ? Value?.ToString(3) : Value?.ToString();


        public override bool TryParseValue(string value, out Version parsedValue) => Version.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record RateValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Rate;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }

    [MemoryPackable]
    public partial record FileValue : BaseValue<byte[]>
    {
        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }

        public override SensorType Type => SensorType.File;

        public override string ShortInfo => GetShortDescription();


        public override bool TryParseValue(string value, out byte[] parsedValue)
        {
            parsedValue = [];
            return false;
        }

        public string FileSizeToNormalString() => OriginalSize.ToReadableMemoryFormat();

        protected override bool IsEqual(BaseValue value) => false;

        private string GetShortDescription()
        {
            string sizeString = FileSizeToNormalString();
            string fileNameString = GetFileNameString();

            return $"File size: {sizeString}. {fileNameString}";
        }

        private string GetFileNameString()
        {
            if (string.IsNullOrEmpty(Extension) && string.IsNullOrEmpty(Name))
                return "No file info specified!";

            if (string.IsNullOrEmpty(Name))
                return $"Extension: {Extension}.";

            if (!string.IsNullOrEmpty(Path.GetExtension(Name)))
                return $"File name: {Name}.";

            return $"File name: {Path.ChangeExtension(Name, Extension)}.";
        }
    }

    /// <summary>
    /// A large file value (ADR-0022, #1883): a value of a File sensor whose content is not part of the value. It holds the
    /// description of the file; the content lies in the large file store under <see cref="FileId"/>. <c>Value</c> is
    /// always empty and <c>OriginalSize</c> is the size of the file. Its time is the time the server accepted the file,
    /// unique within the sensor; the modification time of the source file is <see cref="SourceModified"/>. The state of
    /// the file (uploading, verified, packed) is in the store's record, never in the row, so the row is never rewritten.
    /// <para>
    /// A derived record under a union tag of its own (<see cref="UnionTag"/>), so the rows of ordinary file values keep
    /// their bytes; a server without this type fails on the large rows only: the sensor loads without a value, the rows
    /// stay untouched.
    /// </para>
    /// </summary>
    [MemoryPackable]
    public partial record LargeFileValue : FileValue
    {
        /// <summary>The MemoryPack union tag of the record on <see cref="BaseValue"/>; the first byte of its row.</summary>
        public const byte UnionTag = 12;

        /// <summary>The id of the file in the large file store, made by the server.</summary>
        public Guid FileId { get; init; }

        /// <summary>SHA-256 of the original content of the file, found by the server's end check of the upload (32 bytes).</summary>
        public byte[] Hash { get; init; }

        /// <summary>The modification time of the source file, UTC.</summary>
        public DateTime SourceModified { get; init; }


        /// <summary>True when a stored row holds a large file value: recognised by its union tag, without deserializing it.</summary>
        public static bool IsLargeFileRow(ReadOnlySpan<byte> row) => row.Length > 0 && row[0] == UnionTag;
    }

    [MemoryPackable]
    public partial record IntegerBarValue : BarBaseValue<int>
    {
        public override SensorType Type => SensorType.IntegerBar;
    }

    [MemoryPackable]
    public partial record DoubleBarValue : BarBaseValue<double>
    {
        public override SensorType Type => SensorType.DoubleBar;
    }

    [MemoryPackable]
    public partial record EnumValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Enum;

        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }
}