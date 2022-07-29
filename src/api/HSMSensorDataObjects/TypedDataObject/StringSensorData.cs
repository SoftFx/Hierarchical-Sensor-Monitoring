using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [Obsolete("Use StringSensorValue")]
    [DataContract]
    public class StringSensorData
    {
        [DataMember]
        public string StringValue { get; set; }
        [DataMember]
        public string Comment { get; set; }
    }
}
