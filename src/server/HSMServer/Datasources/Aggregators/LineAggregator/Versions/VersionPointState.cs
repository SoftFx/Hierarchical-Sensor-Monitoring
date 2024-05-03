using System;

namespace HSMServer.Datasources.Aggregators
{
    public readonly struct VersionPointState : ILinePointState<Version>
    {
        public DateTime Time { get; init; }

        public Version Value { get; init; }


        public long Count { get; init; } = 1;


        public VersionPointState() { }
    }
}