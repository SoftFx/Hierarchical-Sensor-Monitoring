using System;
using HSMSensorDataObjects;

namespace HSMServer.Model.SensorsData
{
    public class SensorData
    {
        public string Path { get; set; }
        public string Product { get; set; }
        public SensorType SensorType { get; set; }
        public DateTime Time { get; set; }
        public string ShortValue { get; set; }
        public SensorStatus Status { get; set; }
    }
}
