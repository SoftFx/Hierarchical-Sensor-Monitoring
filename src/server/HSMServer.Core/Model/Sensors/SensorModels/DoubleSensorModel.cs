using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        public override SensorType Type { get; } = SensorType.Double;

        public override DoubleValuesStorage Storage { get; }


        internal DoubleSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new DoubleValuesStorage() { Database = db };
        }
    }
}
