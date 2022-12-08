using HSMSensorDataObjects.Swagger;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorValue : ValueBase<byte[]>
    {
        [DataMember]
        [DefaultValue((int)SensorType.FileSensor)]
        public override SensorType Type => SensorType.FileSensor;

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
