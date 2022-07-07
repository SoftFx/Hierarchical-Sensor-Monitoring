using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        protected override StringValuesStorage Storage { get; }

        public override SensorType Type { get; } = SensorType.String;


        internal StringSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal StringSensorModel(SensorEntity entity, IDatabaseCore db)
            : base(entity)
        {
            Storage = new StringValuesStorage() { Database = db };
        }
    }
}
