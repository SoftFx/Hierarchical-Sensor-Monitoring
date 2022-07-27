using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [Obsolete]
    [DataContract]
    public class UnitedSensorValue
    {
        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public DateTime Time { get; set; }

        [DataMember]
        public string Comment { get; set; }

        [DataMember]
        [DefaultValue((int)SensorStatus.Ok)]
        public SensorStatus Status { get; set; } = SensorStatus.Ok;

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public SensorType Type { get; set; }

        [DataMember]
        public string Data { get; set; }

        public bool IsBarSensor()
        {
            return Type == SensorType.DoubleBarSensor || Type == SensorType.IntegerBarSensor;
        }
    }
}
