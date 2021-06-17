using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    public class FileSensorData
    {
        [DataMember]
        public string Comment { get; set; }
        [DataMember]
        public string Extension { get; set; }
        [DataMember]
        public string FileContent { get; set; }
        [DataMember]
        public string FileName { get; set; }
    }
}
