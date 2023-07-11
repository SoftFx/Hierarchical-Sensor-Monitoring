namespace HSMServer.Core.Model.HistoryValues
{
    public sealed class FileSensorHistory : SensorHistory
    {
        public byte[] Value { get; init; }

        public string FileName { get; init; }

        public string Extension { get; init; }
    }
}
