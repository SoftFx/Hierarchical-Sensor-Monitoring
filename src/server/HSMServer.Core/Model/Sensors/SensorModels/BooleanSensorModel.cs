using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        internal override BooleanValuesStorage Storage { get; } = new BooleanValuesStorage();


        public override SensorPolicyCollection<BooleanValue, BooleanPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.Boolean;


        public BooleanSensorModel(SensorEntity entity) : base(entity) { }
    }
}
