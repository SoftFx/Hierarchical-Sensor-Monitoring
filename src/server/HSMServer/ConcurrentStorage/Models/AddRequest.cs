using System;

namespace HSMServer.ConcurrentStorage
{
    public abstract record BaseAddRequest : BaseRequest
    {
        public required Guid AuthorId { get; init; }
    }
}
