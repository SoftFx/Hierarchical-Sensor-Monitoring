using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Notifications;
using System;
using System.Collections.Concurrent;
using Telegram.Bot.Types;

namespace HSMServer.Notification.Settings
{
    [Obsolete("Should be removed after telegram chats migration")]
    public sealed class TelegramSettings
    {
        public ConcurrentDictionary<ChatId, TelegramChat> Chats { get; } = new();


        [Obsolete("Should be removed after telegram chats migration")]
        internal TelegramSettings(TelegramSettingsEntityOld entity)
        {
            if (entity == null)
                return;

            if (entity.Chats != null)
                foreach (var chat in entity.Chats)
                    Chats.TryAdd(new(chat.Id), new TelegramChat(chat));
        }
    }
}
