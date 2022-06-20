using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class SensorEntity
    {
        public string Id { get; set; }
        public string ProductId { get; set; }
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Description { get; set; }
        public int SensorType { get; set; }
        public long ExpectedUpdateIntervalTicks { get; set; }
        public string Unit { get; set; }
        public List<ValidationParameterEntity> ValidationParameters { get; set; }

        [JsonIgnore]
        public bool IsConverted { get; set; }
    }
}