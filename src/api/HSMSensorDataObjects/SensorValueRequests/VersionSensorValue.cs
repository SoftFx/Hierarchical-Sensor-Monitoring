using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class VersionSensorValue : SensorValueBase<Version>
    {
        [DefaultValue((int)SensorType.VersionSensor)]
        public override SensorType Type => SensorType.VersionSensor;
    }
}