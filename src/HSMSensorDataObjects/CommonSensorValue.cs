using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects
{
    [DataContract]
    [Obsolete("08.07.2021. Use UnitedSensorValue as common value object")]
    public class CommonSensorValue
    {
        [DataMember]
        public SensorType SensorType { get; set; }
        [DataMember]
        public string TypedValue { get; set; }
    }
}
