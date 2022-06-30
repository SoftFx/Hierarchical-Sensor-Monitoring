using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>
    {
        public override SensorType Type { get; } = SensorType.DoubleBar;

        public override DoubleBarValuesStorage Storage { get; }


        internal DoubleBarSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new DoubleBarValuesStorage(db);
        }
    }
}
