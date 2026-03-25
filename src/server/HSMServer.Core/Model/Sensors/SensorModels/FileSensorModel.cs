using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;


namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        protected override FileValuesStorage Storage { get; } = new FileValuesStorage();


        public override SensorPolicyCollection<FileValue, FilePolicy> Policies { get; }

        public override SensorType Type { get; } = SensorType.File;


        public FileSensorModel(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider provider) : base(entity, database)
        {
            Policies = new(provider);
            Policies.Attach(this);
        }
    }
}
