using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class EnumSensorValue : SensorValueBase<int>
    {
        [DefaultValue((int)SensorType.EnumSensor)]
        public override SensorType Type => SensorType.EnumSensor;
    }
}
