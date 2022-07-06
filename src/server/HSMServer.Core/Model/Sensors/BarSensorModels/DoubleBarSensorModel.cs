using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>
    {
        protected override DoubleBarValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.DoubleBar;


        internal DoubleBarSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal DoubleBarSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new DoubleBarValuesStorage() { Database = db };
        }
    }
}
