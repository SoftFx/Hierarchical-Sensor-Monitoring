using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>
    {
        public override IntegerBarValuesStorage Storage { get; } = new();


        internal IntegerBarSensorModel(SensorEntity entity) : base(entity) { }
    }
}
