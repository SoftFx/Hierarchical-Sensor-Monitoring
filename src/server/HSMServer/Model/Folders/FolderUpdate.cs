using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using System;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public record FolderUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }

        public required InitiatorInfo Initiator { get; init; }


        public TimeIntervalViewModel TTL { get; init; }

        public TimeIntervalViewModel KeepHistory { get; init; }

        public TimeIntervalViewModel SelfDestroy { get; init; }


        public string Description { get; init; }

        public Color? Color { get; init; }

        public string Name { get; init; }
    }
}
