using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorValue : SensorValueBase
    {
        [DataMember]
        public string Extension { get; set; }
        [DataMember]
        public string FileContent { get; set; }
        [DataMember]
        public string FileName { get; set; }
    }
}
