using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Notifications.Telegram.AddressBook;
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

        private DateTime _nextSendMessageTime;


        internal HashSet<Guid> Folders { get; } = [];


        internal ScheduleBuilder ScheduleMessageBuilder { get; } = new();

        internal MessageBuilder MessageBuilder { get; } = new();


        public ChatId ChatId { get; init; }

        public ConnectedChatType Type { get; init; }

        public DateTime AuthorizationTime { get; init; }


        public int MessagesAggregationTimeSec { get; private set; }

        public bool SendMessages { get; private set; }


        public string Author { get; set; }


        public bool ShouldSendNotification => MessagesAggregationTimeSec > 0 && _nextSendMessageTime <= DateTime.UtcNow;


        public TelegramChat() : base()
        {
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = DefaultMessagesAggregationTimeSec;
        }

        internal TelegramChat(TelegramChatEntity entity) : base(entity)
        {
            ChatId = new(entity.ChatId);

            SendMessages = entity.SendMessages;
            Type = (ConnectedChatType)entity.Type;
            MessagesAggregationTimeSec = entity.MessagesAggregationTimeSec;
            AuthorizationTime = new DateTime(entity.AuthorizationTime);
        }


        protected override void ApplyUpdate(TelegramChatUpdate update)
        {
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


        internal IEnumerable<string> GetNotifications()
        {
            yield return MessageBuilder.GetAggregateMessage();
            yield return ScheduleMessageBuilder.GetReport();

            _nextSendMessageTime = DateTime.UtcNow.Ceil(TimeSpan.FromSeconds(MessagesAggregationTimeSec));
        }
    }
}