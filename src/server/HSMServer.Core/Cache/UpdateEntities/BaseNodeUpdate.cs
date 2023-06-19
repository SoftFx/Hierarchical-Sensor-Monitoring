using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public abstract record BaseNodeUpdate
    {
        public required Guid Id { get; init; }


        public TimeIntervalModel TTL { get; init; }

        public TimeIntervalModel KeepHistory { get; init; }

        public TimeIntervalModel RestoreInterval { get; init; }

        public TimeIntervalModel SelfDestroy { get; init; }

        public string Description { get; init; }
    }
}
