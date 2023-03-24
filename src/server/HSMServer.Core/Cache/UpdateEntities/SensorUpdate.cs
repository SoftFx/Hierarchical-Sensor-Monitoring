using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate : BaseNodeUpdate
    {
        public string Unit { get; init; }

        public SensorState? State { get; init; }

        public DateTime? EndOfMutingPeriod { get; init; }
    }
}
