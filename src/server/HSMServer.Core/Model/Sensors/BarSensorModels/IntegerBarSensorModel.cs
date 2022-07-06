using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>
    {
        protected override IntegerBarValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.IntegerBar;


        internal IntegerBarSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal IntegerBarSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new IntegerBarValuesStorage() { Database = db };
        }
    }
}
