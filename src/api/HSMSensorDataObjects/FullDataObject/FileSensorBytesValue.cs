using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorBytesValue : ValueBase<byte[]>
    {
        [DataMember]
        public override SensorType Type => SensorType.FileSensorBytes;

        [Obsolete]
        public byte[] FileContent 
        { 
            get => Value;
            set => Value = value;
        }

        [DataMember]
        public string Extension { get; set; }

        [DataMember]
        public string FileName { get; set; }
    }
}
