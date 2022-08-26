using System;

namespace HSMServer.Core.Cache.Entities
{
    public class SensorUpdate
    {
        public Guid Id { get; init; }

        public string Description { get; init; }

        public byte ExpectedUpdateIntervalOption { get; init; }

        public long ExpectedUpdateIntervalTicks { get; init; }

        public string Unit { get; init; }
    }
}
