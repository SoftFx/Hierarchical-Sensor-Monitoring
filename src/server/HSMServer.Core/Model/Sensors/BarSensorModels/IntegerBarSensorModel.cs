namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>
    {
        protected override IntegerBarValuesStorage Storage { get; } = new IntegerBarValuesStorage();

        public override SensorType Type { get; } = SensorType.IntegerBar;
    }
}
