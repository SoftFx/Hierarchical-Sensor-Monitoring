using HSMDatabase.AccessManager.DatabaseEntities;
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
        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public ChatId ChatId { get; init; }


        [Obsolete]
        public bool IsUserChat { get; init; }

        public bool SendMessages { get; init; }

        public string Description { get; init; }

        public ConnectedChatType Type { get; init; }

        public DateTime AuthorizationTime { get; init; }

        public int MessagesAggregationTime { get; init; }


        public string Name { get; set; }

        public string Author { get; set; }


        public TelegramChat()
        {
            Id = Guid.NewGuid();
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
            MessagesAggregationTime = entity.MessagesAggregationTime;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        internal TelegramChatEntityOld ToEntityOld() =>
            new()
            {
                SystemId = Id.ToByteArray(),
                Id = ChatId?.Identifier ?? 0L,
                IsUserChat = IsUserChat,
                Name = Name,
                AuthorizationTime = AuthorizationTime.Ticks,
            };

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
                MessagesAggregationTime = MessagesAggregationTime,
            };
    }
}
