using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{ 
    [DataContract]
    public class IntSensorValue : SensorValueBase
    {
        [DataMember]
        public int IntValue { get; set; }
    }
}
