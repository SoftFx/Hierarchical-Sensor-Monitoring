using System;

namespace HSMServer.DataLayer.Model
{
    public class SensorDataObject
    {
        public DateTime Time { get; set; }
        public long Timestamp { get; set; }
        public string Path { get; set; }
        public SensorDataTypes DataType { get; set; }
        public string TypedData { get; set; }
        public DateTime TimeCollected { get; set; }
    }
}
