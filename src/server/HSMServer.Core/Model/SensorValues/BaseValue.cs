using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Core.Model
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
        [Display(Name = "per sec")]
        PerSecond = 0,
        [Display(Name = "per min")]
        PerMinute = 1,
        [Display(Name = "per hour")]
        PerHour = 2,
        [Display(Name = "per day")]
        PerDay = 3,
        [Display(Name = "per week")]
        PerWeek = 4,
        [Display(Name = "per month")]
        PerMonth = 5
    }


    public abstract record BaseValue
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
        public DateTime LastUpdateTime => LastReceivingTime ?? ReceivingTime;


        [JsonIgnore]
        public virtual SensorType Type { get; } //abstract not work with JsonIgnore, so use virtual

        [JsonIgnore]
        public virtual object RawValue { get; }

        [JsonIgnore]
        public virtual string ShortInfo { get; }


        public abstract BaseValue TrySetValue(string str);

        public abstract BaseValue TrySetValue(BaseValue baseValue);
        

        internal bool TryAggregateValue(BaseValue value)
        {
            if (IsEqual(value))
            {
                LastReceivingTime = value.ReceivingTime;
                AggregatedValuesCount++;

                return true;
            }

            return false;
        }

        internal SensorValueEntity ToEntity(Guid sensorId) =>
            new()
            {
                SensorId = sensorId.ToString(),
                ReceivingTime = ReceivingTime.Ticks,
                Value = this,
            };

        protected virtual bool IsEqual(BaseValue value) => (Status, Comment) == (value.Status, value.Comment);
    }


    public abstract record BaseInstantValue : BaseValue
    {
        public double? EmaValue { get; init; }
    }


    public abstract record BaseValue<T> : BaseInstantValue
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