using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications.Telegram
{
    public sealed class TelegramChat
    {
        public ChatId Id { get; init; }

        public string Name { get; init; }

        public bool IsUserChat { get; init; }

        public DateTime AuthorizationTime { get; init; }


        public TelegramChat() { }

        internal TelegramChat(TelegramChatEntity entity)
        {
            Id = new(entity.Id);
            IsUserChat = entity.IsUserChat;
            Name = entity.Name;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        internal TelegramChatEntity ToEntity() =>
            new()
            {
                Id = Id?.Identifier ?? 0L,
                IsUserChat = IsUserChat,
                Name = Name,
                AuthorizationTime = AuthorizationTime.Ticks,
            };
    }
}
