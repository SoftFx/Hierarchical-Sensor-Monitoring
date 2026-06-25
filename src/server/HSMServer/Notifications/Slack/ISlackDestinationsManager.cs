using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Notifications
{
    public interface ISlackDestinationsManager : IConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>
    {
        string GetSlackDestinationName(Guid id);
    }
}
