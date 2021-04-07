using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class DoubleSensorValue : SensorValueBase
    {
        [DataMember]
        public double DoubleValue { get; set; }
    }
}
