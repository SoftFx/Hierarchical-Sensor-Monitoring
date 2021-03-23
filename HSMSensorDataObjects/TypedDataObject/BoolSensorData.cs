using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class BoolSensorData
    {
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public bool BoolValue { get; set; }
        [DataMember]
        public SensorStatus Status { get; set; }
    }
}
