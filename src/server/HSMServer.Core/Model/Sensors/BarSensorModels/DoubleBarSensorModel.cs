using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarSensorModel : BaseSensorModel<DoubleBarValue>, IBarSensor
    {
        internal override DoubleBarValuesStorage Storage { get; } = new DoubleBarValuesStorage();


        public override DataPolicyCollection<DoubleBarValue, DoubleBarDataPolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.DoubleBar;


        BarBaseValue IBarSensor.LocalLastValue => Storage.PartialLastValue;


        public DoubleBarSensorModel(SensorEntity entity) : base(entity) { }
    }
}
