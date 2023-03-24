namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        protected override DoubleValuesStorage Storage { get; } = new DoubleValuesStorage();

        public override SensorType Type { get; } = SensorType.Double;
    }
}
