using HSMCommon.Extensions;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Storages;
using System;
using System.IO;

namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool>
    {
        public override SensorType Type => SensorType.Boolean;


        public override bool TryParseValue(string value, out bool parsedValue) => bool.TryParse(value, out parsedValue);
    }


    public record IntegerValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Integer;


        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }


    public record DoubleValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Double;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }


    public record StringValue : BaseValue<string>
    {
        public override SensorType Type => SensorType.String;


        public override bool TryParseValue(string value, out string parsedValue)
        {
            parsedValue = value;

            return !string.IsNullOrWhiteSpace(value);
        }
    }


    public record TimeSpanValue : BaseValue<TimeSpan>
    {
        public override SensorType Type => SensorType.TimeSpan;

        public override object RawValue => Value.Ticks;


        public override bool TryParseValue(string value, out TimeSpan parsedValue) => TimeSpan.TryParse(value, out parsedValue);
    }


    public record VersionValue : BaseValue<Version>
    {
        public override SensorType Type => SensorType.Version;

        public override string ShortInfo => Value?.Revision == 0 ? Value?.ToString(3) : Value?.ToString();


        public override bool TryParseValue(string value, out Version parsedValue) => Version.TryParse(value, out parsedValue);
    }


    public record RateValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Rate;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }


    public record FileValue : BaseValue<byte[]>
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


    public record IntegerBarValue : BarBaseValue<int>
    {
        public override SensorType Type => SensorType.IntegerBar;
    }


    public record DoubleBarValue : BarBaseValue<double>
    {
        public override SensorType Type => SensorType.DoubleBar;
    }

    public record EnumValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Enum;

        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }
}