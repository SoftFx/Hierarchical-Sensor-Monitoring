using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        protected override BooleanValuesStorage Storage { get; } = new BooleanValuesStorage();

        public override SensorType Type { get; } = SensorType.Boolean;


        public BooleanSensorModel(SensorEntity entity) : base(entity) { }
    }
}
