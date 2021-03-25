using System;
using HSMSensorDataObjects;

namespace HSMServer.DataLayer.Model
{
    public class SensorDataObject
    {
        public DateTime Time { get; set; }
        public long Timestamp { get; set; }
        public string Path { get; set; }
        public SensorType DataType { get; set; }
        public string TypedData { get; set; }
        public DateTime TimeCollected { get; set; }
        public SensorStatus Status { get; set; }
    }
}