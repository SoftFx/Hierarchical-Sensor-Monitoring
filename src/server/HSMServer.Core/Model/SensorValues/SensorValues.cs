using System;
using System.IO;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.Boolean;
    }


    public record IntegerValue : BaseValue<int>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.Integer;
    }


    public record DoubleValue : BaseValue<double>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.Double;
    }


    public record StringValue : BaseValue<string>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.String;
    }


    public record TimeSpanValue : BaseValue<TimeSpan>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.TimeSpan;

        [JsonIgnore]
        public override object RawValue => Value.Ticks;
    }


    public record VersionValue : BaseValue<Version>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.Version;

        [JsonIgnore]
        public override string ShortInfo => Value.Revision == 0 ? Value.ToString(3) : Value.ToString();
    }


    public record FileValue : BaseValue<byte[]>
    {
        private const double SizeDenominator = 1024.0;


        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }


        [JsonIgnore]
        public override SensorType Type => SensorType.File;

        [JsonIgnore]
        public override string ShortInfo => GetShortDescription();


        private string GetShortDescription()
        {
            string sizeString = FileSizeToNormalString();
            string fileNameString = GetFileNameString();

            return $"File size: {sizeString}. {fileNameString}";
        }

        public string FileSizeToNormalString()
        {
            const int maxGBCounter = 3;

            int counter = 0;
            double size = OriginalSize;

            while (size > SizeDenominator && counter++ < maxGBCounter)
                size /= SizeDenominator;

            string units = counter switch
            {
                0 => "bytes",
                1 => "KB",
                2 => "MB",
                _ => "GB",
            };

            return $"{size:F2} {units}";
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
        [JsonIgnore]
        public override SensorType Type => SensorType.IntegerBar;
    }


    public record DoubleBarValue : BarBaseValue<double>
    {
        [JsonIgnore]
        public override SensorType Type => SensorType.DoubleBar;
    }
}