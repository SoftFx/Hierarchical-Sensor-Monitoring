using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [Obsolete]
    [DataContract]
    public class UnitedSensorValue : StringSensorValue
    {
        [DataMember]
        public new SensorType Type { get; set; }

        [DataMember]
        public string Data 
        { 
            get => Value;
            set => Value = value;
        }

        public bool IsBarSensor()
        {
            return Type == SensorType.DoubleBarSensor || Type == SensorType.IntegerBarSensor;
        }
    }
}
