using System;

namespace HSMServer.Core
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
        public DateTime ReceivingTime { get; } = DateTime.UtcNow;

        public string Key { get; init; }

        public string Path { get; init; }

        public string Comment { get; init; }

        public DateTime Time { get; init; }

        // TODO: if this property is necessary
        //public SensorType Type { get; init; }

        public SensorStatus Status { get; init; }
    }


    public abstract record BaseValue<T> : BaseValue
    {
        public T Value { get; init; }
    }


    public abstract record BarBaseValue<T> : BaseValue
    {
        public T Min { get; init; }

        public T Max { get; init; }

        public T Mean { get; init; }

        public T LastValue { get; init; }

        public int Count { get; init; }

        public DateTime OpenTime { get; init; }

        public DateTime CloseTime { get; init; }
    }
}
