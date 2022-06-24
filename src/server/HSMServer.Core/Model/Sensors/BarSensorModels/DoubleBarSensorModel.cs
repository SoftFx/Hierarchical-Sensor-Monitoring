using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>
    {
        public override DoubleBarValuesStorage Storage { get; } = new();


        internal DoubleBarSensorModel(SensorEntity entity) : base(entity) { }
    }
}
