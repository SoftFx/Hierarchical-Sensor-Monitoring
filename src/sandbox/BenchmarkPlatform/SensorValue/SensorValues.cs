using HSMCommon.Extensions;
using MemoryPack;
using System;
using System.IO;


namespace HSMServer.Core.Model
{

    public partial record BooleanValue : BaseValue<bool>
    {
        public override SensorType Type => SensorType.Boolean;


        public override bool TryParseValue(string value, out bool parsedValue) => bool.TryParse(value, out parsedValue);
    }


    public partial record IntegerValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Integer;


        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }


    public partial record DoubleValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Double;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }


    public partial record StringValue : BaseValue<string>
    {
        public override SensorType Type => SensorType.String;


        public override bool TryParseValue(string value, out string parsedValue)
        {
            parsedValue = value;

            return !string.IsNullOrWhiteSpace(value);
        }
    }


    public partial record TimeSpanValue : BaseValue<TimeSpan>
    {
        public override SensorType Type => SensorType.TimeSpan;

        public override object RawValue => Value.Ticks;


        public override bool TryParseValue(string value, out TimeSpan parsedValue) => TimeSpan.TryParse(value, out parsedValue);
    }


    public partial record VersionValue : BaseValue<Version>
    {
        public override SensorType Type => SensorType.Version;

        public override string ShortInfo => Value?.Revision == 0 ? Value?.ToString(3) : Value?.ToString();


        public override bool TryParseValue(string value, out Version parsedValue) => Version.TryParse(value, out parsedValue);
    }


    public partial record RateValue : BaseValue<double>
    {
        public override SensorType Type => SensorType.Rate;


        public override bool TryParseValue(string value, out double parsedValue) => double.TryParse(value, out parsedValue);
    }

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


    public partial record IntegerBarValue : BarBaseValue<int>
    {
        public override SensorType Type => SensorType.IntegerBar;
    }

    public partial record DoubleBarValue : BarBaseValue<double>
    {
        public override SensorType Type => SensorType.DoubleBar;
    }


    public partial record EnumValue : BaseValue<int>
    {
        public override SensorType Type => SensorType.Enum;

        public override bool TryParseValue(string value, out int parsedValue) => int.TryParse(value, out parsedValue);
    }



    // Простой DTO без наследования
    [MemoryPackable]
    public partial record BooleanValueDto
    {
        [MemoryPackOrder(0)]
        public DateTime ReceivingTime { get; set; }

        [MemoryPackOrder(1)]
        public byte Status { get; set; }

        [MemoryPackOrder(2)]
        public string Comment { get; set; }

        [MemoryPackOrder(3)]
        public DateTime Time { get; set; }

        [MemoryPackOrder(4)]
        public bool IsTimeout { get; set; }

        [MemoryPackOrder(5)]
        public DateTime? LastReceivingTime { get; set; }

        [MemoryPackOrder(6)]
        public long AggregatedValuesCount { get; set; }

        [MemoryPackOrder(7)]
        public double? EmaValue { get; set; }

        [MemoryPackOrder(8)]
        public bool Value { get; set; }

        // Оптимизация: использовать byte для Type
        [MemoryPackOrder(9)]
        public byte Type { get; set; }

        public static BooleanValueDto FromBooleanValue(BooleanValue value) => new()
        {
            ReceivingTime = value.ReceivingTime,
            Status = (byte)value.Status,
            Comment = value.Comment,
            Time = value.Time,
            IsTimeout = value.IsTimeout,
            LastReceivingTime = value.LastReceivingTime,
            AggregatedValuesCount = value.AggregatedValuesCount,
            EmaValue = value.EmaValue,
            Value = value.Value,
            Type = (byte)SensorType.Boolean
        };

        public BooleanValue ToBooleanValue() => new()
        {
            ReceivingTime = ReceivingTime,
            Status = (SensorStatus)Status,
            Comment = Comment,
            Time = Time,
            IsTimeout = IsTimeout,
            LastReceivingTime = LastReceivingTime,
            AggregatedValuesCount = AggregatedValuesCount,
            EmaValue = EmaValue,
            Value = Value
        };
    }

}