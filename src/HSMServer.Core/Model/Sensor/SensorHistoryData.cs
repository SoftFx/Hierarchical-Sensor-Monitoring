using System;
using HSMSensorDataObjects;

namespace HSMServer.Core.Model.Sensor
{
    public class SensorHistoryData
    {
        public DateTime Time { get; set; }
        public SensorType SensorType { get; set; }
        public string TypedData { get; set; }
        public int OriginalFileSensorContentSize { get; set; }
    }
}
