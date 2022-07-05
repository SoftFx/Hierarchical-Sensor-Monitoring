using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        public override SensorType Type { get; } = SensorType.File;

        public override FileValuesStorage Storage { get; }


        internal FileSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal FileSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new FileValuesStorage() { Database = db };
        }
    }
}
