using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model
{
    public enum SensorStatus : byte
    {
        Ok,
        Warning,
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
        Version
    }


    public abstract record BaseValue
    {
        public DateTime ReceivingTime { get; init; } = DateTime.UtcNow;

        public SensorStatus Status { get; init; }

        public string Comment { get; init; }

        public DateTime Time { get; init; }


        [JsonIgnore]
        public virtual SensorType Type { get; } //abstract not work with JsonIgnore, so use virtual

        [JsonIgnore]
        public virtual object RawValue { get; }

        [JsonIgnore]
        public virtual string ShortInfo { get; }


        internal SensorValueEntity ToEntity(Guid sensorId) =>
            new()
            {
                SensorId = sensorId.ToString(),
                ReceivingTime = ReceivingTime.Ticks,
                Value = this,
            };
    }


    public abstract record BaseValue<T> : BaseValue
    {
        public T Value { get; init; }


        public override string ShortInfo => Value?.ToString();

        public override object RawValue => Value;
    }
}