using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Storages.ValueStorages;

namespace HSMServer.Core.Model
{
    internal class CounterSensorModel : BaseSensorModel<CounterValue>
    {
        internal override CounterValuesStorage Storage { get; } = new CounterValuesStorage();


        public override SensorPolicyCollection<CounterValue, CounterPolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Counter;


        public CounterSensorModel(SensorEntity entity) : base(entity) { }
    }
}