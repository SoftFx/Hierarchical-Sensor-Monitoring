using System;

namespace HSMServer.Core.Model
{
    public enum SensorStatus : byte
    {
        Ok,
        Warning,
        Error,
        Unknown = byte.MaxValue,
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
    }


    public abstract record BaseValue
    {
        public DateTime ReceivingTime { get; init; } = DateTime.UtcNow;

        public string Comment { get; init; }

        public DateTime Time { get; init; }

        // TODO: if this property is necessary
        //public SensorType Type { get; init; }

        public SensorStatus Status { get; init; }


        public abstract string ShortInfo { get; }
    }


    public abstract record BaseValue<T> : BaseValue
    {
        public T Value { get; init; }

        public override string ShortInfo => Value.ToString();
    }
}
