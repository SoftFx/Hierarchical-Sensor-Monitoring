namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        protected override StringValuesStorage Storage { get; } = new StringValuesStorage();

        public override SensorType Type { get; } = SensorType.String;


        protected override void InitializeDefaultPolicies() =>
            _policies.Add(new StringValueLengthPolicy());
    }
}
