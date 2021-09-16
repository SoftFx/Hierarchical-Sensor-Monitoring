using HSMDatabase.Entity;

namespace HSMServer.Core.Model
{
    public class SensorInfo
    {
        public string Path { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Description { get; set; }

        public SensorInfo() {}
        public SensorInfo(SensorEntity entity)
        {
            if (entity == null) return;

            ProductName = entity.ProductName;
            Path = entity.Path;
            SensorName = entity.SensorName;
            Description = entity.Description;
        }
    }
}
