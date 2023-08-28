using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Text.Json.Serialization;

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
        Enum
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

        public abstract bool TryParseValue(string value, out T parsedValue);
    }
}