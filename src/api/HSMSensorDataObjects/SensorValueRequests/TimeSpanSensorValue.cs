using System;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class TimeSpanSensorValue : SensorValueBase<long>
    {
        [DefaultValue((int)SensorType.TimeSpanSensor)]
        public override SensorType Type => SensorType.TimeSpanSensor;
    }
}