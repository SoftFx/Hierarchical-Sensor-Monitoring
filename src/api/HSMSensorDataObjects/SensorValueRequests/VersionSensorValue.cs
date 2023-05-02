using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class VersionSensorValue : SensorValueBase<string>
    {
        [DefaultValue((int)SensorType.VersionSensor)]
        public override SensorType Type => SensorType.VersionSensor;
    }
}