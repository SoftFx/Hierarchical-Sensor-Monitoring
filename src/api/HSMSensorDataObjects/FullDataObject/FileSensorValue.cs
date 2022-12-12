using System.ComponentModel;

namespace HSMSensorDataObjects.FullDataObject
{
    public class FileSensorValue : SensorValueBase<byte[]>
    {
        [DefaultValue((int)SensorType.FileSensor)]
        public override SensorType Type => SensorType.FileSensor;

        [DefaultValue("txt")]
        public string Extension { get; set; }

        public string Name { get; set; }
    }
}
