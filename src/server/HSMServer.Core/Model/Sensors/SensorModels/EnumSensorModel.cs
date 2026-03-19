using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;


namespace HSMServer.Core.Model
{
    public sealed class EnumSensorModel : BaseSensorModel<EnumValue>
    {
        protected override EnumValuesStorage Storage { get; } = new EnumValuesStorage();


        public override SensorPolicyCollection<EnumValue, EnumPolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.Enum;


        public EnumSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }

    }
}
