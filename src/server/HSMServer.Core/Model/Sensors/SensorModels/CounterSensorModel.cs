using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Storages.ValueStorages;

namespace HSMServer.Core.Model
{
    internal sealed class RateSensorModel : BaseSensorModel<RateValue>
    {
        internal override RateValuesStorage Storage { get; } = new RateValuesStorage();


        public override SensorPolicyCollection<RateValue, RatePolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.Rate;


        public RateSensorModel(SensorEntity entity) : base(entity) { }
    }
}