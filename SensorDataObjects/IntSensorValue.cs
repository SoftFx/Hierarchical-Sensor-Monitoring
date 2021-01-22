using System;

namespace SensorDataObjects
{
    public class IntSensorValue
    {
        public int IntValue { get; set; }
        public string Key { get; set; }
        public string Path { get; set; }
        public DateTime Time { get; set; }
        public string Comment { get; set; }
    }
}
