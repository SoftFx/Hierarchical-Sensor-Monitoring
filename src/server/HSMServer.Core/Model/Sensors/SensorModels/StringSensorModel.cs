using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        internal override StringValuesStorage Storage { get; } = new StringValuesStorage();


        public override DataPolicyCollection<StringValue, StringDataPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.String;


        public StringSensorModel(SensorEntity entity) : base(entity) { }
    }
}
