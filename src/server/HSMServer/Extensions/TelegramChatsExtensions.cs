using HSMServer.Notifications;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    internal static class TelegramChatsExtensions
    {
        internal static List<TelegramChat> GetGroups(this List<TelegramChat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramGroup);

        internal static List<TelegramChat> GetPrivates(this List<TelegramChat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramPrivate);


        private static List<TelegramChat> GetChats(this List<TelegramChat> chats, ConnectedChatType type) =>
            chats.Where(ch => ch.Type == type).ToList();
    }
}
