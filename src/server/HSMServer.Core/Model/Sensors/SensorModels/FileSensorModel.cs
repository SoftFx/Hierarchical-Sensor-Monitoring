using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        internal override FileValuesStorage Storage { get; } = new FileValuesStorage();


        public override SensorPolicyCollection<FileValue, FilePolicy> DataPolicies { get; } = new();

        public override SensorType Type { get; } = SensorType.File;


        public FileSensorModel(SensorEntity entity) : base(entity) { }
    }
}
