using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public interface ITelegramChatsManager : IConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>
    {
    }
}
