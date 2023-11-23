using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IUpdateRequest
    {
        Guid Id { get; }

        string Name { get; }
    }


    public abstract record BaseUpdateRequest : BaseRequest, IUpdateRequest
    {
        public required Guid Id { get; init; }
    }
}