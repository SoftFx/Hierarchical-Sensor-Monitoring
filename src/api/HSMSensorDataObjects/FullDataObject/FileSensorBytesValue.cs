using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorBytesValue : ValueBase<byte[]>
    {
        [DataMember]
        [DefaultValue((int)SensorType.FileSensorBytes)]
        public override SensorType Type => SensorType.FileSensorBytes;

        [Obsolete]
        [SwaggerExclude]
        public byte[] FileContent
        {
            get => Value;
            set => Value = value;
        }

        [DataMember]
        [DefaultValue("txt")]
        public string Extension { get; set; }

        [DataMember]
        public string FileName { get; set; }
    }
}
