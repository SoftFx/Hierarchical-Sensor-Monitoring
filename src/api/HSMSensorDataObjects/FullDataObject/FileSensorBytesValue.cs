using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorBytesValue : SensorValueBase
    {
        [DataMember]
        public string Extension { get; set; }
        [DataMember]
        public byte[] FileContent { get; set; }
        [DataMember]
        public string FileName { get; set; }
    }
}
