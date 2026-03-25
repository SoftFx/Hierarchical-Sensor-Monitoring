using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;


namespace HSMServer.Core.Model
{
    public sealed class StringSensorModel : BaseSensorModel<StringValue>
    {
        protected override StringValuesStorage Storage { get; } = new StringValuesStorage();


        public override SensorPolicyCollection<StringValue, StringPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.String;


        public StringSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
