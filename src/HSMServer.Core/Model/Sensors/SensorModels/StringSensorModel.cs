namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        public override StringValuesStorage Storage { get; } = new();
    }
}
