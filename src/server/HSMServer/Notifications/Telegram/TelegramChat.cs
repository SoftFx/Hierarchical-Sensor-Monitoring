using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications.Telegram
{
    public sealed class TelegramChat
    {
        public Guid SystemId { get; set; } // TODO: remove setter after TelegramChat migration

        public ChatId Id { get; init; }


        public string Name { get; set; }

        public bool IsUserChat { get; init; }

        public DateTime AuthorizationTime { get; init; }


        public TelegramChat()
        {
            SystemId = Guid.NewGuid();
        }

        internal TelegramChat(TelegramChatEntity entity)
        {
            Id = new(entity.Id);
            IsUserChat = entity.IsUserChat;
            Name = entity.Name;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);

            if (entity.SystemId is not null) // TODO: remove this check after TelegramChat migration
                SystemId = new Guid(entity.SystemId);
        }


        internal TelegramChatEntity ToEntity() =>
            new()
            {
                SystemId = SystemId.ToByteArray(),
                Id = Id?.Identifier ?? 0L,
                IsUserChat = IsUserChat,
                Name = Name,
                AuthorizationTime = AuthorizationTime.Ticks,
            };
    }
}
