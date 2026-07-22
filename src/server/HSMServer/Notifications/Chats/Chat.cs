using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;
using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications.Chats
{
    public sealed class Chat : BaseServerModel<ChatEntity, ChatUpdate>
    {
        internal const bool DefaultSendMessages = true;
        internal const int DefaultMessagesAggregationTimeSec = 60;

        private readonly ChannelAccumulator _telegramAccumulator = new();
        private readonly ChannelAccumulator _slackAccumulator = new();
        private readonly ChannelAccumulator _mattermostAccumulator = new();


        internal HashSet<Guid> Folders { get; } = [];


        // Common
        public int MessagesAggregationTimeSec { get; private set; }

        public bool SendMessages { get; private set; }

        public string Author { get; set; }


        // Telegram (optional)
        public ChatId? TelegramChatId { get; private set; }

        // init would block the ClearTelegramBinding path — ApplyUpdate needs to null these out so
        // ToEntity() drops them on the next round-trip. internal set keeps existing object
        // initializers in ChatsManager.TryConnect and ChatsManagerTests working; outside this
        // assembly the surface is effectively read-only.
        public ConnectedChatType? TelegramType { get; internal set; }

        public DateTime? AuthorizationTime { get; internal set; }


        // Telegram title/description mirrored from the bot on every start (see
        // TelegramBot.ChatNamesSynchronization). Distinct from Name/Description, which are
        // admin-owned via EditChat and never overwritten by sync.
        public string TelegramChatTitle { get; private set; }

        public string TelegramChatDescription { get; private set; }


        // Slack (optional)
        public string SlackWebhookUrl { get; private set; }


        // Mattermost (optional)
        public string MattermostWebhookUrl { get; private set; }


        // Per-channel accumulators. Returns null when the channel is not configured for this chat,
        // so callers can early-skip without an extra flag check. Each accumulator owns its own
        // MessageBuilder/ScheduleBuilder/next-send timer — sharing them across channels would
        // double-buffer each alert and let the first channel to flush starve the others.
        internal ChannelAccumulator TelegramAccumulator => TelegramChatId is null ? null : _telegramAccumulator;

        internal ChannelAccumulator SlackAccumulator => string.IsNullOrEmpty(SlackWebhookUrl) ? null : _slackAccumulator;

        internal ChannelAccumulator MattermostAccumulator => string.IsNullOrEmpty(MattermostWebhookUrl) ? null : _mattermostAccumulator;


        public Chat(ChatId chatId) : base()
        {
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = DefaultMessagesAggregationTimeSec;

            TelegramChatId = chatId;
            TelegramType = ConnectedChatType.TelegramPrivate;
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

            TelegramChatTitle = entity.TelegramChatTitle;
            TelegramChatDescription = entity.TelegramChatDescription;

            SlackWebhookUrl = entity.SlackWebhookUrl;
            MattermostWebhookUrl = entity.MattermostWebhookUrl;
        }


        public void UpdateChatId(ChatId chatId) => TelegramChatId = chatId;


        protected override void ApplyUpdate(ChatUpdate update)
        {
            SendMessages = update.SendMessages ?? SendMessages;
            MessagesAggregationTimeSec = update.MessagesAggregationTimeSec ?? MessagesAggregationTimeSec;

            TelegramChatTitle = update.TelegramChatTitle ?? TelegramChatTitle;
            TelegramChatDescription = update.TelegramChatDescription ?? TelegramChatDescription;

            SlackWebhookUrl = update.SlackWebhookUrl ?? SlackWebhookUrl;
            MattermostWebhookUrl = update.MattermostWebhookUrl ?? MattermostWebhookUrl;

            if (update.TelegramChatId.HasValue)
                TelegramChatId = new ChatId(update.TelegramChatId.Value);

            if (update.ClearTelegramBinding is true)
            {
                TelegramChatId = null;
                TelegramType = null;
                AuthorizationTime = null;
            }

            if (update.ClearSlackWebhook is true)
                SlackWebhookUrl = null;

            if (update.ClearMattermostWebhook is true)
                MattermostWebhookUrl = null;
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

            entity.TelegramChatTitle = TelegramChatTitle;
            entity.TelegramChatDescription = TelegramChatDescription;

            entity.SlackWebhookUrl = SlackWebhookUrl;
            entity.MattermostWebhookUrl = MattermostWebhookUrl;

            return entity;
        }
    }
}
