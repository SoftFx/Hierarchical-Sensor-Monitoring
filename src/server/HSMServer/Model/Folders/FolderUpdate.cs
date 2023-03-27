using HSMServer.ConcurrentStorage;
using System;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public record FolderUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }

        public string Description { get; init; }

        public Color? Color { get; init; }
    }
}
