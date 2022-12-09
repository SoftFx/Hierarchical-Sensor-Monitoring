using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public abstract class BarSensorValueBase : SensorValueBase
    {
        [DataMember]
        public DateTime OpenTime { get; set; }

        [DataMember]
        public DateTime CloseTime { get; set; }

        [DataMember]
        public int Count { get; set; }
    }


    [DataContract]
    public abstract class BarValueSensorBase<T> : BarSensorValueBase
    {
        [DataMember]
        public T Min { get; set; }

        [DataMember]
        public T Max { get; set; }

        [DataMember]
        public T Mean { get; set; }

        [DataMember]
        public T LastValue { get; set; }

        [DataMember]
        public Dictionary<double, T> Percentiles { get; set; }


        public BarValueSensorBase()
        {
            Percentiles = new Dictionary<double, T>();
        }
    }
}
