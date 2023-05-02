using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IUpdateModel
    {
        Guid Id { get; }

        string Name { get; }
    }
}
