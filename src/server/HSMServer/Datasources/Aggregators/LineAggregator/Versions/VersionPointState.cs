using System;

namespace HSMServer.Datasources.Aggregators
{
    public readonly struct VersionPointState
    {
        public DateTime Time { get; init; }

        public Version Value { get; init; }


        public long Count { get; init; }


        public VersionPointState(Version value, DateTime time)
        {
            Value = value;
            Time = time;

            Count = 1;
        }
    }
}