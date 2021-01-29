using System;

namespace HSMSensorDataObjects
{
    public interface ISensorValue
    {
        public string Key { get; set; }
        public string Path { get; set; }
        public DateTime Time { get; set; }
        public string Comment { get; set; }
    }
}