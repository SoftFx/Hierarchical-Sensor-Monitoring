using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public readonly struct AccessKeyUpdate
    {
        public AccessKeyUpdate(Guid id, KeyState state)
        {
            Id = id;
            DisplayName = null;
            Comment = null;
            Permissions = null;
            State = state;
        }
        
        public Guid Id { get; init; }

        public string DisplayName { get; init; }

        public string Comment { get; init; }

        public KeyPermissions? Permissions { get; init; }

        public KeyState? State { get; init; }
    }
}
