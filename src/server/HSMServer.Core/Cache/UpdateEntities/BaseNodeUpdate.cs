using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public abstract record BaseNodeUpdate
    {
        public Guid Id { get; init; } //TODO return required after migration policies


        public TimeIntervalModel KeepHistory { get; init; }

        public TimeIntervalModel SelfDestroy { get; init; }

        public TimeIntervalModel TTL { get; init; }


        public InitiatorInfo Initiator { get; init; } = InitiatorInfo.System;


        public string Description { get; init; }


        public PolicyUpdate TTLPolicy { get; init; }
    }
}
