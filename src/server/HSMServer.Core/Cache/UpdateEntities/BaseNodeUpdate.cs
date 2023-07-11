using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public abstract record BaseNodeUpdate
    {
        public required Guid Id { get; init; }


        public TimeIntervalModel ExpectedUpdateInterval { get; init; }

        public TimeIntervalModel SavedHistoryPeriod { get; init; }

        public TimeIntervalModel RestoreInterval { get; init; }

        public TimeIntervalModel SelfDestroy { get; init; }

        public string Description { get; init; }
    }
}
