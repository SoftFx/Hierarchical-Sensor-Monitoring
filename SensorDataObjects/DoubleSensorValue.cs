using System;

namespace SensorDataObjects
{
    public class DoubleSensorValue
    {
        public double DoubleValue { get; set; }
        public string Key { get; set; }
        public string Path { get; set; }
        public DateTime Time { get; set; }
        public string Comment { get; set; }
    }
}
