using System;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class SensorDataEntity
    {
        public string Id { get; set; }
        public DateTime Time { get; set; }
        public long Timestamp { get; set; }
        public string Path { get; set; }
        public byte DataType { get; set; }
        public string TypedData { get; set; }
        public DateTime TimeCollected { get; set; }
        public byte Status { get; set; }
        public int OriginalFileSensorContentSize { get; set; }
    }
}