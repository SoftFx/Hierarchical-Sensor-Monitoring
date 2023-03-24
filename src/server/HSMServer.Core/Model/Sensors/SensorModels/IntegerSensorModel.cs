using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        protected override IntegerValuesStorage Storage { get; } = new IntegerValuesStorage();

        public override SensorType Type { get; } = SensorType.Integer;


        public IntegerSensorModel(SensorEntity entity) : base(entity) { }
    }
}
