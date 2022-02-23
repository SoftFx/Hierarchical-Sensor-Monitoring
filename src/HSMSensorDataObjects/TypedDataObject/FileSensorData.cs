using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.TypedDataObject
{
    [DataContract]
    [Obsolete("FileSensorData is obsolete. New FileSensorValues are replaced by FileSensorBytesValues in API, saved FileSensorValues in db are converted to FileSensorBytesValues in Core")]
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
