using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.FullDataObject
{
    [DataContract]
    public class FileSensorValue : SensorValueBase<byte[]>
    {
        [DataMember]
        [DefaultValue((int)SensorType.FileSensor)]
        public override SensorType Type => SensorType.FileSensor;

        [DataMember]
        [DefaultValue("txt")]
        public string Extension { get; set; }

        [DataMember]
        public string FileName { get; set; }
    }
}
