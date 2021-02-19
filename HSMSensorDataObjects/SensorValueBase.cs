using System;

namespace HSMSensorDataObjects
{
    public abstract class SensorValueBase
    {
        public string Key { get; set; }
        public string Path { get; set; }
        public DateTime Time { get; set; }
        public string Comment { get; set; }
    }
}
