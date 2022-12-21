using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class TimeSpanSensorValue : SensorValueBase<TimeSpan>
    {
        [DefaultValue((int)SensorType.StringSensor)]
        public override SensorType Type => SensorType.TimeSpanSensor;
    }
}