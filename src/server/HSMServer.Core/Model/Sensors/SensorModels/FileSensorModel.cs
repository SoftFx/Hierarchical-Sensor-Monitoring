using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        protected override FileValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.File;


        internal FileSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal FileSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new FileValuesStorage() { Database = db };
        }
    }
}
