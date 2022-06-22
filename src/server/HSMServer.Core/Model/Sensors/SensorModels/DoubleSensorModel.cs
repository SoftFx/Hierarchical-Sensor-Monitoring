namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        public override DoubleValuesStorage Storage { get; } = new();
    }
}
