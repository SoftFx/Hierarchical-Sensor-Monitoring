namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        public override BooleanValuesStorage Storage { get; } = new();
    }
}
