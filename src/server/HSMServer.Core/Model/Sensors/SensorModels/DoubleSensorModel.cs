using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class DoubleSensorModel : BaseSensorModel<DoubleValue>
    {
        protected override DoubleValuesStorage Storage { get; } = new DoubleValuesStorage();


        public override DataPolicyCollection<DoubleValue, DoubleDataPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.Double;


        public DoubleSensorModel(SensorEntity entity) : base(entity) { }
    }
}
