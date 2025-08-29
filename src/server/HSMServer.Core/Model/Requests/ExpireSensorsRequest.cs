using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal record ExpireSensorsRequest(Guid ProductId) : IUpdateRequest
    {
        public Guid ProductId { get; } = ProductId;

    }
}
