using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed class ProductUpdate
    {
        public Guid Id { get; init; }
        
        public string Description { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }
    }
}
