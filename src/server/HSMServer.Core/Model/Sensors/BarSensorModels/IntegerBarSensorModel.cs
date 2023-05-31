using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarSensorModel : BaseSensorModel<IntegerBarValue>, IBarSensor
    {
        protected override IntegerBarValuesStorage Storage { get; } = new IntegerBarValuesStorage();


        public override DataPolicyCollection<IntegerBarValue, IntegerBarDataPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.IntegerBar;


        BarBaseValue IBarSensor.LocalLastValue => Storage.PartialLastValue;


        public IntegerBarSensorModel(SensorEntity entity) : base(entity) { }
    }
}
