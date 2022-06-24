using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        public override BooleanValuesStorage Storage { get; } = new();


        internal BooleanSensorModel(SensorEntity entity) : base(entity) { }
    }
}
