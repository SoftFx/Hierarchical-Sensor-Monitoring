using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class StringSensorValue : SensorValueBase
    {
        [DataMember]
        public string StringValue { get; set; }
    
    }
}
