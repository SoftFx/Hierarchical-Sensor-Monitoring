using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        protected override BooleanValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.Boolean;


        internal BooleanSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal BooleanSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new BooleanValuesStorage() { Database = db };
        }
    }
}
