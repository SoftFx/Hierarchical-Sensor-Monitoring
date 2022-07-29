using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
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
        [DefaultValue((int)SensorType.FileSensor)]
        public override SensorType Type => SensorType.FileSensor;

        [DataMember]
        [DefaultValue("txt")]
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

        [SwaggerExclude]
        public override byte[] Value
        {
            get => base.Value;
            set => base.Value = value;
        }


        [DataMember]
        public string FileName { get; set; }
    }
}
