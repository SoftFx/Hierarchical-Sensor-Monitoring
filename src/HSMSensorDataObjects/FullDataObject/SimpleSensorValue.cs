using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class SimpleSensorValue : SensorValueBase
    {
        [DataMember]
        public string Data { get; set; }
    }
}
