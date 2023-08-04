using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        internal override DoubleValuesStorage Storage { get; } = new DoubleValuesStorage();


        public override SensorPolicyCollection<DoubleValue, DoublePolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Double;


        public DoubleSensorModel(SensorEntity entity) : base(entity) { }
    }
}
