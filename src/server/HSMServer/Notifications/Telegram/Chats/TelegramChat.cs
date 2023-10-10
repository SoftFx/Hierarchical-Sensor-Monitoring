﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    public enum ConnectedChatType : byte
    {
        TelegramPrivate = 0,
        TelegramGroup = 1,
    }


    public sealed class TelegramChat : IServerModel<TelegramChatEntity, TelegramChatUpdate>
    {
        private const bool DefaultSendMessages = true;
        private const int DefaultMessagesAggregationTimeSec = 60;


        internal MessageBuilder MessageBuilder { get; } = new();


        public Guid Id { get; init; } // TODO: should be just get after telegram chats migration

        public ChatId ChatId { get; init; }

        public Guid? AuthorId { get; init; }

        public ConnectedChatType Type { get; init; }

        public DateTime AuthorizationTime { get; init; }

        [Obsolete("Should be removed after telegram chats migration")]
        public bool IsUserChat { get; init; }


        public bool SendMessages { get; init; }

        public int MessagesAggregationTimeSec { get; init; }


        public string Description { get; init; }

        public string Author { get; set; }

        public string Name { get; set; }


        public TelegramChat()
        {
            Id = Guid.NewGuid();
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = DefaultMessagesAggregationTimeSec;
        }

        internal TelegramChat(TelegramChatEntityOld entity)
        {
            ChatId = new(entity.Id);
            Id = new Guid(entity.SystemId);
            IsUserChat = entity.IsUserChat;
            Name = entity.Name;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }

        internal TelegramChat(TelegramChatEntity entity)
        {
            Id = new Guid(entity.Id);
            ChatId = new(entity.ChatId);
            AuthorId = entity.Author is not null ? new Guid(entity.Author) : null;

            Name = entity.Name;
            Description = entity.Description;
            SendMessages = entity.SendMessages;
            Type = (ConnectedChatType)entity.Type;
            MessagesAggregationTimeSec = entity.MessagesAggregationTimeSec;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        public void Update(TelegramChatUpdate update)
        {
            throw new NotImplementedException();
        }

        public TelegramChatEntity ToEntity() =>
            new()
            {
                Name = Name,
                Type = (byte)Type,
                Id = Id.ToByteArray(),
                Description = Description,
                SendMessages = SendMessages,
                Author = AuthorId?.ToByteArray(),
                ChatId = ChatId?.Identifier ?? 0L,
                AuthorizationTime = AuthorizationTime.Ticks,
                MessagesAggregationTimeSec = MessagesAggregationTimeSec,
            };
    }
}