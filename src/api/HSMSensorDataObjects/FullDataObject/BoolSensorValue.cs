using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class BoolSensorValue : SensorValueBase
    {
        [DataMember]
        public bool BoolValue { get; set; }
    }
}
