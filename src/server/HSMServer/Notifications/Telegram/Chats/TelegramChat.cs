using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    public enum ConnectedChatType : byte
    {
        [Display(Name = "direct")]
        TelegramPrivate = 0,
        [Display(Name = "group")]
        TelegramGroup = 1,
    }


    public sealed class TelegramChat : BaseServerModel<TelegramChatEntity, TelegramChatUpdate>
    {
        private const bool DefaultSendMessages = true;
        private const int DefaultMessagesAggregationTimeSec = 60;


        internal HashSet<Guid> Folders { get; } = new();

        internal MessageBuilder MessageBuilder { get; } = new();


        public ChatId ChatId { get; init; }

        public ConnectedChatType Type { get; init; }

        public DateTime AuthorizationTime { get; init; }

        [Obsolete("Should be removed after telegram chats migration")]
        public bool IsUserChat { get; init; }


        public bool SendMessages { get; private set; }

        public int MessagesAggregationTimeSec { get; private set; }


        public string Author { get; set; }


        public TelegramChat() : base()
        {
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

        internal TelegramChat(TelegramChatEntity entity) : base(entity)
        {
            ChatId = new(entity.ChatId);

            SendMessages = entity.SendMessages;
            Type = (ConnectedChatType)entity.Type;
            MessagesAggregationTimeSec = entity.MessagesAggregationTimeSec;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        public override void Update(TelegramChatUpdate update)
        {
            base.Update(update);

            SendMessages = update.SendMessages ?? SendMessages;
            MessagesAggregationTimeSec = update.MessagesAggregationTimeSec ?? MessagesAggregationTimeSec;
        }

        public override TelegramChatEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.Type = (byte)Type;
            entity.SendMessages = SendMessages;
            entity.ChatId = ChatId?.Identifier ?? 0L;
            entity.AuthorizationTime = AuthorizationTime.Ticks;
            entity.MessagesAggregationTimeSec = MessagesAggregationTimeSec;

            return entity;
        }
    }
}