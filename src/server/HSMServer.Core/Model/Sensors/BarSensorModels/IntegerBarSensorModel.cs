namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>
    {
        public override IntegerBarValuesStorage Storage { get; } = new();
    }
}
