namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        protected override IntegerValuesStorage Storage { get; } = new IntegerValuesStorage();

        public override SensorType Type { get; } = SensorType.Integer;
    }
}
