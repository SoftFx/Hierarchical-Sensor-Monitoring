using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        protected override FileValuesStorage Storage { get; } = new FileValuesStorage();

        public override SensorType Type { get; } = SensorType.File;


        public FileSensorModel(SensorEntity entity) : base(entity) { }
    }
}
