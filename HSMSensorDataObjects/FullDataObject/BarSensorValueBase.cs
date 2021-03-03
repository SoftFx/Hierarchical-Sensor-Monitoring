using System;

namespace HSMSensorDataObjects.FullDataObject
{
    public abstract class BarSensorValueBase : SensorValueBase
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Count { get; set; }
    }
}
