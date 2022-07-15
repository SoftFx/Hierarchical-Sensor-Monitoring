namespace HSMServer.Core.Model
{
    public sealed class FileSensorModel : BaseSensorModel<FileValue>
    {
        protected override FileValuesStorage Storage { get; } = new FileValuesStorage();

        public override SensorType Type { get; } = SensorType.File;


        internal FileValue GetValue() => Storage.GetDecompressedLatestValue();
    }
}
