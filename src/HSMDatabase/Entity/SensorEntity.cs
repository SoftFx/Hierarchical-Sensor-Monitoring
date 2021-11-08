using System;
using System.Collections.Generic;

namespace HSMDatabase.Entity
{
    public class SensorEntity
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Description { get; set; }
        public int SensorType { get; set; }
        public TimeSpan ExpectedUpdateInterval { get; set; }
        public string Unit { get; set; }
        public List<ValidationParameterEntity> ValidationParameters { get; set; }
    }
}