using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        internal override IntegerValuesStorage Storage { get; } = new IntegerValuesStorage();


        public override SensorPolicyCollection<IntegerValue, IntegerPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.Integer;


        public IntegerSensorModel(SensorEntity entity) : base(entity) { }
    }
}
