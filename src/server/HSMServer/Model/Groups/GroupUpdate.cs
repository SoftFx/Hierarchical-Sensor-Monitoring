using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Model.Groups
{
    public class GroupUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }
    }
}
