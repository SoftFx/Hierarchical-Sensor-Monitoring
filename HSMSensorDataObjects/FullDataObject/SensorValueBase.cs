using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public abstract class SensorValueBase
    {
        [DataMember]
        public string Key { get; set; }
        [DataMember]
        public string Path { get; set; }
        [DataMember]
        public DateTime Time { get; set; }
        [DataMember]
        public string Comment { get; set; }
    }
}
