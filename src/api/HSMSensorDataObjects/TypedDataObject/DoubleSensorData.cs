using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [Obsolete("Use DoubleSensorValue")]
    [DataContract]
    public class DoubleSensorData
    {
        [DataMember]
        public double DoubleValue { get; set; }
        [DataMember]
        public string Comment { get; set; }
    }
}
