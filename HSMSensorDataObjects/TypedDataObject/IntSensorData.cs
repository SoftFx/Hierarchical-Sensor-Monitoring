using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class IntSensorData
    {
        [DataMember]
        public int IntValue { get; set; }
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public SensorStatus Status { get; set; }
    }
}
