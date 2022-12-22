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
        TimeSpan
    }


    public abstract record BaseValue
    {
        public DateTime ReceivingTime { get; init; } = DateTime.UtcNow;

        public string Comment { get; init; }

        public DateTime Time { get; init; }

        public SensorStatus Status { get; init; }


        [JsonIgnore]
        public abstract SensorType Type { get; }

        [JsonIgnore]
        public abstract string ShortInfo { get; }


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

        [JsonIgnore]
        public override string ShortInfo => Value.ToString();
    }
}
