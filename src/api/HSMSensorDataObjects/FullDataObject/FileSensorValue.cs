using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    [Obsolete("FileSensorValue is obsolete. New FileSensorValues are replaced by FileSensorBytesValues in API, saved FileSensorValues in db are converted to FileSensorBytesValues in Core")]
    public class FileSensorValue : ValueBase<string>
    {
        [DataMember]
        public override SensorType Type => SensorType.FileSensor;

        [DataMember]
        public string Extension { get; set; }
        [DataMember]
        public string FileContent { get; set; }
        [DataMember]
        public string FileName { get; set; }
    }
}
