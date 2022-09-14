using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.Entities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public class SensorUpdate
    {
        public Guid Id { get; init; }

        public string Description { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }

        public string Unit { get; init; }

        public SensorState? State { get; init; }
    }
}
