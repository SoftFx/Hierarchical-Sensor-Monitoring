using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;

namespace HSMServer.Notifications
{
    public sealed class SlackDestinationsManager : ConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>, ISlackDestinationsManager
    {
        private readonly IDatabaseCore _database;


        protected override Action<SlackDestinationEntity> AddToDb => _database.AddSlackDestination;

        protected override Action<SlackDestinationEntity> UpdateInDb => _database.UpdateSlackDestination;

        protected override Action<SlackDestination> RemoveFromDb => destination => _database.RemoveSlackDestination(destination.Id.ToByteArray());

        protected override Func<List<SlackDestinationEntity>> GetFromDb => _database.GetSlackDestinations;


        public SlackDestinationsManager(IDatabaseCore database) => _database = database;


        protected override SlackDestination FromEntity(SlackDestinationEntity entity) => new(entity);
    }
}
