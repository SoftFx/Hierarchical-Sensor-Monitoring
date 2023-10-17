using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.ConcurrentStorage
{
    public record RemoveModel
    {
        public required Guid Id { get; init; }

        public InitiatorInfo Initiator { get; init; } = InitiatorInfo.System;
    }
}
