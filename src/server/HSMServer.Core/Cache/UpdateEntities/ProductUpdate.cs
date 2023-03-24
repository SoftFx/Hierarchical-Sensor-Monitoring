using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed record ProductUpdate : BaseNodeUpdate
    {
        public Guid? FolderId { get; init; }
    }
}
