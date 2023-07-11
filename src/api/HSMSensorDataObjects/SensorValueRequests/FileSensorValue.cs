using System.Collections.Generic;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class FileSensorValue : SensorValueBase<List<byte>>
    {
        [DefaultValue((int)SensorType.FileSensor)]
        public override SensorType Type => SensorType.FileSensor;

        [DefaultValue("txt")]
        public string Extension { get; set; }

        public string Name { get; set; }
    }
}
