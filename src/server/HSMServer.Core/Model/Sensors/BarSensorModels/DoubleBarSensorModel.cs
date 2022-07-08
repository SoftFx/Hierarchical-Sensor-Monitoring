namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>
    {
        protected override DoubleBarValuesStorage Storage { get; } = new DoubleBarValuesStorage();

        public override SensorType Type { get; } = SensorType.DoubleBar;
    }
}
