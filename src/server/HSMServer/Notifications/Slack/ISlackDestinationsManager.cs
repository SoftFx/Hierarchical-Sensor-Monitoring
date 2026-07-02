using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public interface ISlackDestinationsManager : IConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>
    {
        string GetSlackDestinationName(Guid id);

        void AddFolderToChats(Guid folderId, List<Guid> destinations);

        Task RemoveFolderFromChats(Guid folderId, List<Guid> destinations, InitiatorInfo initiator);

        void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator);
    }
}
