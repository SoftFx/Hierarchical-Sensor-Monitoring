using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        protected override DoubleValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.Double;


        internal DoubleSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal DoubleSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new DoubleValuesStorage() { Database = db };
        }
    }
}
