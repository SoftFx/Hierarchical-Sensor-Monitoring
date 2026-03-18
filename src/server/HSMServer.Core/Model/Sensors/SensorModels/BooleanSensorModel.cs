using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;


namespace HSMServer.Core.Model
{
    public sealed class BooleanSensorModel : BaseSensorModel<BooleanValue>
    {
        protected override BooleanValuesStorage Storage { get; } = new BooleanValuesStorage();


        public override SensorPolicyCollection<BooleanValue, BooleanPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.Boolean;


        public BooleanSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
