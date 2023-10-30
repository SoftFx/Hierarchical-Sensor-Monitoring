using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IUpdateRequest
    {
        Guid Id { get; }

        string Name { get; }
    }


    public abstract record BaseUpdateRequest : IUpdateRequest
    {
        public required Guid Id { get; init; }


        public string Name { get; init; }

        public string Description { get; init; }
    }
}