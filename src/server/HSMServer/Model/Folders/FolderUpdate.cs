using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Model.Folders
{
    public class FolderUpdate : IUpdateModel
    {
        public required Guid Id { get; init; }
    }
}
