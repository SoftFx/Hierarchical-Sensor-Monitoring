using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed class ProductUpdate
    {
        public required Guid Id { get; init; }

        public string Description { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }

        public Guid? GroupId { get; init; }
    }
}
