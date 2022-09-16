using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramChat
    {
        public ChatId Id { get; init; }

        public bool IsGroup { get; init; }

        public string UserNickname { get; init; }

        public DateTime AuthorizationTime { get; init; }


        internal TelegramChat() { }

        internal TelegramChat(TelegramChatEntity entity)
        {
            Id = new(entity.Id);
            IsGroup = entity.IsGroup;
            UserNickname = entity.UserNickname;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        internal TelegramChatEntity ToEntity() =>
            new()
            {
                Id = Id?.Identifier ?? 0,
                IsGroup = IsGroup,
                UserNickname = UserNickname,
                AuthorizationTime = AuthorizationTime.Ticks,
            };
    }
}
