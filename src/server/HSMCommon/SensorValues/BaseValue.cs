using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MemoryPack;


namespace HSMCommon.Model
{
    public enum SensorStatus : byte
    {
        Ok,
        Error,
        OffTime = byte.MaxValue,
    }


    public enum SensorType : byte
    {
        Boolean,
        Integer,
        Double,
        String,
        IntegerBar,
        DoubleBar,
        File,
        TimeSpan,
        Version,
        Rate,
        Enum,
    }


    /// <summary>
    /// Display units for Rate sensors
    /// </summary>
    public enum RateDisplayUnit
    {
        [Display(Name = "# per sec")]
        PerSecond = 0,
        [Display(Name = "# per min")]
        PerMinute = 1,
        [Display(Name = "# per hour")]
        PerHour = 2,
        [Display(Name = "# per day")]
        PerDay = 3,
        [Display(Name = "# per week")]
        PerWeek = 4,
        [Display(Name = "# per month")]
        PerMonth = 5
    }

    [MemoryPackable]
    [MemoryPackUnion(0, typeof(BarBaseValue))]
    [MemoryPackUnion(1, typeof(BooleanValue))]
    [MemoryPackUnion(2, typeof(IntegerValue))]
    [MemoryPackUnion(3, typeof(DoubleValue))]
    [MemoryPackUnion(4, typeof(StringValue))]
    [MemoryPackUnion(5, typeof(TimeSpanValue))]
    [MemoryPackUnion(6, typeof(VersionValue))]
    [MemoryPackUnion(7, typeof(RateValue))]
    [MemoryPackUnion(8, typeof(FileValue))]
    [MemoryPackUnion(9, typeof(EnumValue))]
    [MemoryPackUnion(10, typeof(IntegerBarValue))]
    [MemoryPackUnion(11, typeof(DoubleBarValue))]
    public abstract partial record BaseValue
    {
        private DateTime _time;

        public DateTime ReceivingTime { get; init; } = DateTime.UtcNow;

        [JsonConverter(typeof(SensorStatusJsonConverter))]
        public SensorStatus Status { get; init; }

        public string Comment { get; init; }

        public DateTime Time
        {
            get => _time.ToUniversalTime();
            set => _time = value.ToUniversalTime();
        }

        public bool IsTimeout { get; init; }

        public DateTime? LastReceivingTime { get; set; }

        public long AggregatedValuesCount { get; set; } = 1;


        [JsonIgnore]
        [MemoryPackIgnore]
        public DateTime LastUpdateTime => LastReceivingTime ?? ReceivingTime;


        [JsonIgnore]
        [MemoryPackIgnore]
        public virtual SensorType Type { get; } //abstract not work with JsonIgnore, so use virtual

        [JsonIgnore]
        [MemoryPackIgnore]
        public virtual object RawValue { get; }

        [JsonIgnore]
        [MemoryPackIgnore]
        public virtual string ShortInfo { get; }

        public abstract BaseValue TrySetValue(string str);

        public abstract BaseValue TrySetValue(BaseValue baseValue);


        public bool TryAggregateValue(BaseValue value)
        {
            if (IsEqual(value))
            {
                LastReceivingTime = value.ReceivingTime;
                AggregatedValuesCount++;

                return true;
            }

            return false;
        }

        protected virtual bool IsEqual(BaseValue value) => (Status, Comment) == (value.Status, value.Comment);
    }

    public abstract partial record BaseInstantValue : BaseValue
    {
        public double? EmaValue { get; init; }
    }

    public abstract partial record BaseValue<T> : BaseInstantValue
    {
        public T Value { get; init; }

        public override string ShortInfo => Value?.ToString();

        public override object RawValue => Value;

        public abstract bool TryParseValue(string value, out T parsedValue);


        public override BaseValue TrySetValue(string newValue)
        {
            if (TryParseValue(newValue, out var parsedValue))
                return this with
                {
                    Value = parsedValue
                };

            return this;
        }

        public override BaseValue TrySetValue(BaseValue baseValue) => this with
        {
            Value = ((BaseValue<T>)baseValue).Value
        };

        protected override bool IsEqual(BaseValue value)
        {
            return base.IsEqual(value) &&
                value is BaseValue<T> other &&
                EqualityComparer<T>.Default.Equals(Value, other.Value);
        }
    }
}