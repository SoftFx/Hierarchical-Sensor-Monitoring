using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.SensorsUpdatesQueue;
using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed record ProductUpdate : BaseNodeUpdate, IUpdateRequest
    {
        public string Name { get; init; }

        public Guid? FolderId { get; init; }

        public PolicyDestinationSettings DefaultChats { get; init; }


        [SetsRequiredMembers]
        public ProductUpdate() : base() { }
    }
}
