using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>
    {
        public override SensorType Type { get; } = SensorType.IntegerBar;

        public override IntegerBarValuesStorage Storage { get; }


        internal IntegerBarSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new IntegerBarValuesStorage() { Database = db };
        }
    }
}
