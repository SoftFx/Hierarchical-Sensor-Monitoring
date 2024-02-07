namespace HSMServer.Core.StatisticInfo
{
    public sealed record SensorHistoryInfo
    {
        public long ValuesSizeBytes { get; init; }

        public long KeysSizeBytes { get; init; }

        public long DataCount { get; init; }


        public long TotalSizeBytes => KeysSizeBytes + ValuesSizeBytes;
    }
}