using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>, IBarSensor
    {
        protected override DoubleBarValuesStorage Storage { get; } = new DoubleBarValuesStorage();

        public override SensorType Type { get; } = SensorType.DoubleBar;

        BarBaseValue IBarSensor.LocalLastValue => Storage.LocalLastValue;


        public DoubleBarSensorModel(SensorEntity entity) : base(entity) { }
    }
}
