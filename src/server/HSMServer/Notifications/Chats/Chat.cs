using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Notifications.AddressBook;
using System;
using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications.Chats
{
    public sealed class Chat : BaseServerModel<ChatEntity, ChatUpdate>
    {
        private const bool DefaultSendMessages = true;
        private const int DefaultMessagesAggregationTimeSec = 60;

        private DateTime _nextSendMessageTime;


        internal HashSet<Guid> Folders { get; } = [];

        internal ScheduleBuilder ScheduleMessageBuilder { get; } = new();

        internal MessageBuilder MessageBuilder { get; } = new();


        // Common
        public int MessagesAggregationTimeSec { get; private set; }

        public bool SendMessages { get; private set; }

        public string Author { get; set; }


        // Telegram (optional)
        public ChatId? TelegramChatId { get; private set; }

        public ConnectedChatType? TelegramType { get; init; }

        public DateTime? AuthorizationTime { get; init; }


        // Slack (optional)
        public string SlackWebhookUrl { get; private set; }


        // Mattermost (optional, channel not implemented yet)
        public string MattermostWebhookUrl { get; private set; }


        public bool ShouldSendNotification => MessagesAggregationTimeSec > 0 && _nextSendMessageTime <= DateTime.UtcNow;


        public Chat(ChatId chatId) : base()
        {
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = DefaultMessagesAggregationTimeSec;

            TelegramChatId = chatId;
        }

        internal Chat() : base()
        {
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = DefaultMessagesAggregationTimeSec;
        }

        internal Chat(ChatEntity entity) : base(entity)
        {
            SendMessages = entity.SendMessages;
            MessagesAggregationTimeSec = entity.MessagesAggregationTimeSec;

            if (entity.TelegramType.HasValue)
            {
                TelegramType = (ConnectedChatType)entity.TelegramType.Value;
                TelegramChatId = new ChatId(entity.TelegramChatId ?? 0L);
                AuthorizationTime = entity.AuthorizationTime.HasValue
                    ? new DateTime(entity.AuthorizationTime.Value)
                    : null;
            }

            SlackWebhookUrl = entity.SlackWebhookUrl;
            MattermostWebhookUrl = entity.MattermostWebhookUrl;
        }


        public void UpdateChatId(ChatId chatId) => TelegramChatId = chatId;


        protected override void ApplyUpdate(ChatUpdate update)
        {
            SendMessages = update.SendMessages ?? SendMessages;
            MessagesAggregationTimeSec = update.MessagesAggregationTimeSec ?? MessagesAggregationTimeSec;

            SlackWebhookUrl = update.SlackWebhookUrl ?? SlackWebhookUrl;
            MattermostWebhookUrl = update.MattermostWebhookUrl ?? MattermostWebhookUrl;

            if (update.TelegramChatId.HasValue)
                TelegramChatId = new ChatId(update.TelegramChatId.Value);
        }

        public override ChatEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.SendMessages = SendMessages;
            entity.MessagesAggregationTimeSec = MessagesAggregationTimeSec;

            if (TelegramType.HasValue)
            {
                entity.TelegramType = (byte)TelegramType.Value;
                entity.TelegramChatId = TelegramChatId?.Identifier;
                entity.AuthorizationTime = AuthorizationTime?.Ticks;
            }

            entity.SlackWebhookUrl = SlackWebhookUrl;
            entity.MattermostWebhookUrl = MattermostWebhookUrl;

            return entity;
        }


        internal IEnumerable<string> GetNotifications()
        {
            try
            {
                foreach (var report in ScheduleMessageBuilder.GetReports())
                    yield return report;

                yield return MessageBuilder.GetAggregateMessage();
            }
            finally
            {
                _nextSendMessageTime = DateTime.UtcNow.Ceil(TimeSpan.FromSeconds(MessagesAggregationTimeSec));
            }
        }
    }
}
