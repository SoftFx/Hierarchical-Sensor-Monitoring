using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public readonly struct AccessKeyUpdate
    {
        public Guid Id { get; init; }

        public string DisplayName { get; init; }

        public string Comment { get; init; }

        public KeyPermissions? Permissions { get; init; }

        public KeyState? State { get; init; }
    }
}
