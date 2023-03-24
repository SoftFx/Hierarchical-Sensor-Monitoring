using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate
    {
        public required Guid Id { get; init; }

        public string Description { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }

        public string Unit { get; init; }

        public SensorState? State { get; init; }

        public DateTime? EndOfMutingPeriod { get; init; }
    }
}
