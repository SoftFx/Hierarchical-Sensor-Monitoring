using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Model.Folders
{
    public record FolderUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }
    }
}
