using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class DoubleSensorData
    {
        [DataMember]
        public double DoubleValue { get; set; }
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public SensorStatus Status { get; set; }
    }
}
