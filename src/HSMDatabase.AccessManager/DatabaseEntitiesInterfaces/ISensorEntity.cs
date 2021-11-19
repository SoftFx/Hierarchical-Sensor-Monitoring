using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public interface ISensorEntity
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Description { get; set; }
        public int SensorType { get; set; }
        public long ExpectedUpdateIntervalTicks { get; set; }
        public string Unit { get; set; }
        public List<ValidationParameterEntity> ValidationParameters { get; set; }
    }
}
