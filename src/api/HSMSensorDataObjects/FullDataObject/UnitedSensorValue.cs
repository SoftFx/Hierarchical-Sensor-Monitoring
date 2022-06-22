using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class UnitedSensorValue : SensorValueBase
    {
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
