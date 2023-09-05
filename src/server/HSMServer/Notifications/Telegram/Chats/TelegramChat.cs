using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    public sealed class TelegramChat : IServerModel<TelegramChatEntity, TelegramChatUpdate>
    {
        public Guid Id { get; }

        public ChatId ChatId { get; init; }


        public string Name { get; set; }

        public bool IsUserChat { get; init; }

        public DateTime AuthorizationTime { get; init; }


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
            throw new NotImplementedException();
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

        public TelegramChatEntity ToEntity()
        {
            throw new NotImplementedException();
        }
    }
}
