using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        protected override BooleanValuesStorage Storage { get; } = new BooleanValuesStorage();


        public override DataPolicyCollection<BooleanValue, BooleanDataPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.Boolean;


        public BooleanSensorModel(SensorEntity entity) : base(entity) { }
    }
}
