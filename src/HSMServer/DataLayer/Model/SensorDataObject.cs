using System;
using HSMSensorDataObjects;

namespace HSMServer.DataLayer.Model
{
    [Obsolete("19.07.2021. Use SensorDataEntity and convert it to SensorData")]
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