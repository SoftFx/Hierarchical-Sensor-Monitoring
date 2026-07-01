using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
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
        private readonly ITreeValuesCache _cache;


        protected override Action<SlackDestinationEntity> AddToDb => _database.AddSlackDestination;

        protected override Action<SlackDestinationEntity> UpdateInDb => _database.UpdateSlackDestination;

        protected override Action<SlackDestination> RemoveFromDb => destination => _database.RemoveSlackDestination(destination.Id.ToByteArray());

        protected override Func<List<SlackDestinationEntity>> GetFromDb => _database.GetSlackDestinations;


        public SlackDestinationsManager(IDatabaseCore database, ITreeValuesCache cache)
        {
            _database = database;
            _cache = cache;
        }


        public string GetSlackDestinationName(Guid id) => this.GetValueOrDefault(id)?.Name;


        public void AddFolderToChats(Guid folderId, List<Guid> destinations)
        {
            foreach (var id in destinations)
                if (TryGetValue(id, out var destination))
                    destination.Folders.Add(folderId);
        }

        public async Task RemoveFolderFromChats(Guid folderId, List<Guid> destinations, InitiatorInfo initiator)
        {
            foreach (var id in destinations)
                if (TryGetValue(id, out var destination))
                    destination.Folders.Remove(folderId);

            await _cache.RemoveChatsFromPoliciesAsync(folderId, destinations, initiator);
        }

        public void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator) =>
            _ = RemoveFolderFromChats(folder.Id, folder.Chats.ToList(), initiator);


        protected override SlackDestination FromEntity(SlackDestinationEntity entity) => new(entity);
    }
}
