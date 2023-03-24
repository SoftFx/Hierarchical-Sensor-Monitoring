using System;
using System.IO;

namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool>
    {
        public override SensorType Type => SensorType.Boolean;
    }


    public record IntegerValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Integer;
    }


    public record DoubleValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Double;
    }


    public record StringValue : BaseValue<string>
    {
        public override SensorType Type => SensorType.String;
    }


    public record TimeSpanValue : BaseValue<TimeSpan>
    {
        public override SensorType Type => SensorType.TimeSpan;


        public static bool TryParse(string interval, out long ticks)
        {
            var ddString = interval.Split(".");
            var hmsString = ddString[^1].Split(":");

            if (ddString.Length == 2 &&
                hmsString.Length == 3 &&
                int.TryParse(ddString[0], out var days) &&
                int.TryParse(hmsString[0], out var hours) &&
                int.TryParse(hmsString[1], out var minutes) &&
                int.TryParse(hmsString[2], out var seconds))
            {
                ticks = new TimeSpan(days, hours, minutes, seconds).Ticks;
                return true;
            }

            ticks = 0L;
            return false;
        }

        public static string TicksToString(long ticks)
        {
            var timeSpan = TimeSpan.FromTicks(ticks);
            return $"{timeSpan.Days}.{timeSpan.Hours}:{timeSpan.Minutes}:{timeSpan.Seconds}";
        }
    }


    public record FileValue : BaseValue<byte[]>
    {
        private const double SizeDenominator = 1024.0;


        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }


        public override SensorType Type => SensorType.File;

        public override string ShortInfo => GetShortDescription();


        private string GetShortDescription()
        {
            string sizeString = FileSizeToNormalString();
            string fileNameString = GetFileNameString();

            return $"File size: {sizeString}. {fileNameString}";
        }

        private string FileSizeToNormalString()
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
        public override SensorType Type => SensorType.IntegerBar;
    }


    public record DoubleBarValue : BarBaseValue<double>
    {
        public override SensorType Type => SensorType.DoubleBar;
    }
}
