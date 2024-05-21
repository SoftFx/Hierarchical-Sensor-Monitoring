using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class TelegramChatsExtensions
    {
        public static string ToNames(this HashSet<Guid> chatIds, Dictionary<Guid, TelegramChat> availableChats)
        {
            var chats = new List<string>(1 << 2);

            foreach (var id in chatIds)
                if (availableChats.TryGetValue(id, out var chat))
                    chats.Add(chat.Name);

            return string.Join(", ", chats);
        }

        internal static List<TelegramChat> GetGroups(this List<TelegramChat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramGroup);

        internal static List<TelegramChat> GetPrivates(this List<TelegramChat> chats) =>
            chats.GetChats(ConnectedChatType.TelegramPrivate);


        private static List<TelegramChat> GetChats(this List<TelegramChat> chats, ConnectedChatType type) =>
            chats.Where(ch => ch.Type == type).ToList();
    }
}
