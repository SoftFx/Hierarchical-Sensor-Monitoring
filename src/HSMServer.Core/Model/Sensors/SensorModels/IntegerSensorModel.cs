namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        public override IntegerValuesStorage Storage { get; } = new();
    }
}
