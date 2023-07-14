using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public abstract record BaseNodeUpdate
    {
        public Guid Id { get; init; } //TODO return required after migration policies


        public TimeIntervalModel TTL { get; init; }

        public TimeIntervalModel KeepHistory { get; init; }

        public TimeIntervalModel SelfDestroy { get; init; }

        public string Description { get; init; }
    }
}
