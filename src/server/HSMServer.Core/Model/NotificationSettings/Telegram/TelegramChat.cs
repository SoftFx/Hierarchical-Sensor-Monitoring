using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramChat
    {
        public ChatId Id { get; init; }

        public string Name { get; init; }

        public bool IsGroup { get; init; }

        public DateTime AuthorizationTime { get; init; }


        internal TelegramChat() { }

        internal TelegramChat(TelegramChatEntity entity)
        {
            Id = new(entity.Id);
            IsGroup = entity.IsGroup;
            Name = entity.Name;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        internal TelegramChatEntity ToEntity() =>
            new()
            {
                Id = Id?.Identifier ?? 0,
                IsGroup = IsGroup,
                Name = Name,
                AuthorizationTime = AuthorizationTime.Ticks,
            };
    }
}
