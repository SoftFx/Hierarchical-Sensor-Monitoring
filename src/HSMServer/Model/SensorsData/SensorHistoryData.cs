using System;
using HSMSensorDataObjects;

namespace HSMServer.Model.SensorsData
{
    public class SensorHistoryData
    {
        public DateTime Time { get; set; }
        public SensorType SensorType { get; set; }
        public string TypedData { get; set; }
    }
}
