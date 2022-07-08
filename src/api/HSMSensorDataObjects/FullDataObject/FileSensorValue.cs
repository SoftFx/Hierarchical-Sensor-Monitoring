using System;
using System.Runtime.Serialization;
using System.Text;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    [Obsolete("FileSensorValue is obsolete. New FileSensorValues are replaced by FileSensorBytesValues in API, saved FileSensorValues in db are converted to FileSensorBytesValues in Core")]
    public class FileSensorValue : ValueBase<byte[]>
    {
        private string _fileContent;

        [DataMember]
        public override SensorType Type => SensorType.FileSensor;

        [DataMember]
        public string Extension { get; set; }

        [Obsolete]
        [DataMember]
        public string FileContent 
        {
            get => _fileContent;
            set
            {
                Value = Encoding.UTF8.GetBytes(value);
                _fileContent = value;
            }
        }

        [DataMember]
        public string FileName { get; set; }
    }
}
