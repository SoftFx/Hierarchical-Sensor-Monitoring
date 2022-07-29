using HSMSensorDataObjects.Swagger;
using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public abstract class BarSensorValueBase : SensorValueBase
    {
        [Obsolete]
        [SwaggerExclude]
        public DateTime StartTime
        {
            get => OpenTime;
            set => OpenTime = value;
        }

        [Obsolete]
        [SwaggerExclude]
        public DateTime EndTime
        {
            get => CloseTime;
            set => CloseTime = value;
        }

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
    }
}
