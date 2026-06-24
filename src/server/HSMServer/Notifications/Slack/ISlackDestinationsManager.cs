using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public interface ISlackDestinationsManager : IConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>
    {
    }
}
