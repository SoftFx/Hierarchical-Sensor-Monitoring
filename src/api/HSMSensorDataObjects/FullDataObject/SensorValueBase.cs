using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public abstract class SensorValueBase
    {
        [DataMember]
        public abstract SensorType Type { get; }

        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public DateTime Time { get; set; } = DateTime.UtcNow;

        [DataMember]
        public string Comment { get; set; }

        [DataMember]
        public SensorStatus Status { get; set; }

        [Obsolete]
        [DataMember]
        public string Description { get; set; }
    }

    [DataContract]
    public abstract class ValueBase<T> : SensorValueBase
    {
        [DataMember]
        public T Value { get; set; }
    }
}
