using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        public override SensorType Type { get; } = SensorType.Double;

        public override DoubleValuesStorage Storage { get; } = new();


        internal DoubleSensorModel(SensorEntity entity) : base(entity) { }
    }
}
