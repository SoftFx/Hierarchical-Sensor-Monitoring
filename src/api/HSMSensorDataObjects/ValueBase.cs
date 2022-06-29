using System;

namespace HSMSensorDataObjects
{
    public abstract class ValueBase
    {
        public DateTime Time { get; private set; }
        public string Key { get; private set; }
        public string Path { get; private set; }
        public string Comment { get; private set; }
        public SensorStatus Status { get; private set; }
        public SensorType Type { get; private set; }
    }

    public class ValueBase<T> : ValueBase
    {
        public T Value { get; private set; }
    }

    public class BarValueBase<T> : ValueBase
    {
        public DateTime OpenTime { get; private set; }
        public DateTime CloseTime { get; private set; }
        public int Count { get; private set; }
        public T Min { get; private set; }
        public T Max { get; private set; }
        public T Mean { get; private set; }
        public T LastValue { get; private set; }
    }
}
