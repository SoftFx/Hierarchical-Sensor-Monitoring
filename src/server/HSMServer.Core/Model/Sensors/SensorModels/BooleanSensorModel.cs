using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        public override SensorType Type { get; } = SensorType.Boolean;

        public override BooleanValuesStorage Storage { get; } = new();


        internal BooleanSensorModel(SensorEntity entity) : base(entity) { }
    }
}
