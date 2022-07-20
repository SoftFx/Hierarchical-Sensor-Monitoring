namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        internal const int DefaultMaxStringLength = 150;


        protected override StringValuesStorage Storage { get; } = new StringValuesStorage();

        public override SensorType Type { get; } = SensorType.String;
    }
}
