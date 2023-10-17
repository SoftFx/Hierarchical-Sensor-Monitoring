using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IUpdateRequest
    {
        Guid Id { get; }

        string Name { get; }
    }
}
