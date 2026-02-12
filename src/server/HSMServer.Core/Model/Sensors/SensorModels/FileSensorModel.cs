using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        internal override FileValuesStorage Storage { get; }


        public override SensorPolicyCollection<FileValue, FilePolicy> Policies { get; } = new();

        public override SensorType Type { get; } = SensorType.File;


        public FileSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity, database)
        {
           Storage = new FileValuesStorage(_getFirstValue, _getLastValue);
        }
    }
}
