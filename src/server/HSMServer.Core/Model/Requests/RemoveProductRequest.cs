using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;
using System;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record RemoveProductRequest : IUpdateRequest
    {
        public Guid Id { get; init; }

        public InitiatorInfo InitiatorInfo { get; init; }


        public RemoveProductRequest(Guid id, InitiatorInfo initiatorInfo)
        {
            Id = id;
            InitiatorInfo = initiatorInfo;
        }
    }
}
