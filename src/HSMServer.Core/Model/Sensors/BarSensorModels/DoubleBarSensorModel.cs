namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>
    {
        public override DoubleBarValuesStorage Storage { get; } = new();
    }
}
