using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [Obsolete("Use ValueBase<T>")]
    [DataContract]
    public class UnitedSensorValue : SensorValueBase
    {
        [DataMember]
        public override SensorType Type { get; }
        [DataMember]
        public string Data { get; set; }

        public bool IsBarSensor()
        {
            return Type == SensorType.DoubleBarSensor || Type == SensorType.IntegerBarSensor;
        }
    }
}
