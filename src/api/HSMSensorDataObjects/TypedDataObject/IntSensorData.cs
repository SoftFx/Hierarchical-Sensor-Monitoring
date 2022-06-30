using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [Obsolete("Use IntSensorValue")]
    [DataContract]
    public class IntSensorData
    {
        [DataMember]
        public int IntValue { get; set; }
        [DataMember]
        public string Comment { get; set; }
    }
}
