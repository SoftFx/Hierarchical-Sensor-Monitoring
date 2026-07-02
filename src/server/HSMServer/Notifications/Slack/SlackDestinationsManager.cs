using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class SlackDestinationsManager : ConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>, ISlackDestinationsManager
    {
        private readonly IDatabaseCore _database;


        protected override Action<SlackDestinationEntity> AddToDb => _database.AddSlackDestination;

        protected override Action<SlackDestinationEntity> UpdateInDb => _database.UpdateSlackDestination;

        protected override Action<SlackDestination> RemoveFromDb => destination => _database.RemoveSlackDestination(destination.Id.ToByteArray());

        protected override Func<List<SlackDestinationEntity>> GetFromDb => _database.GetSlackDestinations;


        public SlackDestinationsManager(IDatabaseCore database)
        {
            _database = database;
        }


        public string GetSlackDestinationName(Guid id) => this.GetValueOrDefault(id)?.Name;


        public void AddFolderToChats(Guid folderId, List<Guid> destinations)
        {
            foreach (var id in destinations)
                if (TryGetValue(id, out var destination))
                    destination.Folders.Add(folderId);
        }

        public Task RemoveFolderFromChats(Guid folderId, List<Guid> destinations, InitiatorInfo initiator)
        {
            foreach (var id in destinations)
                if (TryGetValue(id, out var destination))
                    destination.Folders.Remove(folderId);

            return Task.CompletedTask;
        }

        public void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator) =>
            _ = RemoveFolderFromChats(folder.Id, folder.Chats.ToList(), initiator);


        protected override SlackDestination FromEntity(SlackDestinationEntity entity) => new(entity);
    }
}
