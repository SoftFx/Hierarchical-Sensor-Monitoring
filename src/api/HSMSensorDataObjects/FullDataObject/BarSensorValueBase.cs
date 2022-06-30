using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [Obsolete("Use BarValueBase<T>")]
    [DataContract]
    public abstract class BarSensorValueBase : SensorValueBase
    {
        [DataMember]
        public DateTime StartTime { get; set; }
        [DataMember]
        public DateTime EndTime { get; set; }
        [DataMember]
        public int Count { get; set; }
    }
}
