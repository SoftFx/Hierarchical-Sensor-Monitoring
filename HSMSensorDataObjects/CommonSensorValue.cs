using System.Runtime.Serialization;

namespace HSMSensorDataObjects
{
    [DataContract]
    public class CommonSensorValue
    {
        [DataMember]
        public SensorType SensorType { get; set; }
        [DataMember]
        public string TypedValue { get; set; }
    }
}
