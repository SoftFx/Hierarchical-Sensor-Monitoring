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
        [Obsolete("11.11.2021. Use corresponding string field")]
        public TimeSpan ExpectedUpdateInterval { get; set; }
        /// <summary>
        /// Use this field instead of TimeSpan for correct serialization/deserialization
        /// </summary>
        public long ExpectedUpdateIntervalTicks { get; set; }
        public string Unit { get; set; }
        public List<ValidationParameterEntity> ValidationParameters { get; set; }
    }
}