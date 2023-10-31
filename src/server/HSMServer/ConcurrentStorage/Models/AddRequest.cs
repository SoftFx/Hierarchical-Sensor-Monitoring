using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IAddRequest
    {
        string Name { get; }

        Guid AuthorId { get; }

        string Description { get; }
    }


    public abstract record BaseAddRequest : IAddRequest
    {
        public string Description { get; init; }

        public required string Name { get; init; }

        public required Guid AuthorId { get; init; }
    }
}
