using HSMServer.Core.TableOfChanges;
using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.ConcurrentStorage
{
    public sealed record RemoveRequest
    {
        public required Guid Id { get; init; }

        public InitiatorInfo Initiator { get; init; } = InitiatorInfo.System;


        [SetsRequiredMembers]
        public RemoveRequest(Guid id, InitiatorInfo initiator = null)
        {
            Id = id;

            if (initiator is not null)
                Initiator = initiator;
        }
    }
}
