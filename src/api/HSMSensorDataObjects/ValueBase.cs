using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects
{
    [DataContract]
    public abstract class ValueBase
    {
        [DataMember]
        public DateTime Time { get; private set; } = DateTime.UtcNow;
        [DataMember]
        public string Key { get; private set; }
        [DataMember]
        public string Path { get; private set; }
        [DataMember]
        public string Comment { get; private set; }
        [DataMember]
        public SensorStatus Status { get; private set; }
        public abstract SensorType Type { get; }
    }

    [DataContract]
    public abstract class ValueBase<T> : ValueBase
    {
        private SensorType _type;

        [DataMember]
        public T Value { get; private set; }
        public abstract override SensorType Type { get; }
    }

    [DataContract]
    public abstract class BarValueBase<T> : ValueBase
    {
        [DataMember]
        public DateTime OpenTime { get; private set; }
        [DataMember]
        public DateTime CloseTime { get; private set; }
        [DataMember]
        public int Count { get; private set; }
        [DataMember]
        public T Min { get; private set; }
        [DataMember]
        public T Max { get; private set; }
        [DataMember]
        public T Mean { get; private set; }
        [DataMember]
        public T LastValue { get; private set; }
        public abstract override SensorType Type { get; }
    }
}
