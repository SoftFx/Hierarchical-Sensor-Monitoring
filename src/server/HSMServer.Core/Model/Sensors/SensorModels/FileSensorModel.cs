using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        public override FileValuesStorage Storage { get; } = new();


        internal FileSensorModel(SensorEntity entity) : base(entity) { }
    }
}
