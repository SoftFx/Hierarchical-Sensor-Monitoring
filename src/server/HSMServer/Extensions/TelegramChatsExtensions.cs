using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class TelegramChatsExtensions
    {
        public static string ToNames(this HashSet<Guid> chatIds, Dictionary<Guid, string> availableChats)
        {
            var chats = new List<string>(1 << 2);

            foreach (var id in chatIds)
                if (availableChats.TryGetValue(id, out var name))
                    chats.Add(name);

            return string.Join(", ", chats);
        }

        internal static List<Chat> GetGroups(this List<Chat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramGroup);

        internal static List<Chat> GetPrivates(this List<Chat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramPrivate);


        private static List<Chat> GetChats(this List<Chat> chats, ConnectedChatType type) =>
            chats.Where(ch => ch.TelegramType == type).ToList();
    }
}
