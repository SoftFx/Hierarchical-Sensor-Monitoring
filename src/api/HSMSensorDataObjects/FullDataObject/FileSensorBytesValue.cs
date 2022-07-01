using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorBytesValue : ValueBase<byte[]>
    {
        [Obsolete]
        public byte[] FileContent 
        { 
            get => Value;
            set { Value = value; FileContent = value; }
        }
        [DataMember]
        public string Extension { get; set; }
        [DataMember]
        public string FileName { get; set; }
        [DataMember]
        public override SensorType Type { get => SensorType.FileSensorBytes; }
    }
}
