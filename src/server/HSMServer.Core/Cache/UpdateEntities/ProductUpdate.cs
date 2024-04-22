using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed record ProductUpdate : BaseNodeUpdate
    {
        public string Name { get; init; }

        public Guid? FolderId { get; init; }


        [SetsRequiredMembers]
        public ProductUpdate() : base() { }
    }
}
