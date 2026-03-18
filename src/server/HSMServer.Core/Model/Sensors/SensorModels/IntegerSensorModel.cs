using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;

namespace HSMServer.Core.Model
{
    public sealed class IntegerSensorModel : BaseSensorModel<IntegerValue>
    {
        protected override IntegerValuesStorage Storage { get; } = new IntegerValuesStorage();


        public override SensorPolicyCollection<IntegerValue, IntegerPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.Integer;


        public IntegerSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
