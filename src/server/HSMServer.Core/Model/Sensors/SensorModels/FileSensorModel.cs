using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        public override SensorType Type { get; } = SensorType.File;

        public override FileValuesStorage Storage { get; } = new();


        internal FileSensorModel(SensorEntity entity) : base(entity) { }
    }
}
