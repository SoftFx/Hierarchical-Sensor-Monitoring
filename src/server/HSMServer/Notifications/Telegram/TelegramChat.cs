﻿using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications.Telegram
{
    public sealed class TelegramChat
    {
        public Guid SystemId { get; }

        public ChatId Id { get; init; }


        public string Name { get; set; }

        public bool IsUserChat { get; init; }

        public DateTime AuthorizationTime { get; init; }


        public TelegramChat()
        {
            SystemId = Guid.NewGuid();
        }

        internal TelegramChat(TelegramChatEntityOld entity)
        {
            Id = new(entity.Id);
            SystemId = new Guid(entity.SystemId);
            IsUserChat = entity.IsUserChat;
            Name = entity.Name;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        internal TelegramChatEntityOld ToEntity() =>
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
