using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public class SensorUpdate
    {
        public Guid Id { get; init; }

        public string Description { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }

        public string Unit { get; init; }
    }
}
