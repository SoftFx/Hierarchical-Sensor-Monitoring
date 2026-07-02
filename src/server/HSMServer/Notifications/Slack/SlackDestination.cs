using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Notifications.AddressBook;
using System;
using System.Collections.Generic;

namespace HSMServer.Notifications
{
    public sealed class SlackDestination : BaseServerModel<SlackDestinationEntity, SlackDestinationUpdate>
    {
        private const bool DefaultSendMessages = true;
        private const int DefaultMessagesAggregationTimeSec = 60;

        private DateTime _nextSendMessageTime;


        internal ScheduleBuilder ScheduleMessageBuilder { get; } = new();

        internal MessageBuilder MessageBuilder { get; } = new();


        public string WebhookUrl { get; private set; }

        public int MessagesAggregationTimeSec { get; private set; }

        public bool SendMessages { get; private set; }

        internal HashSet<Guid> Folders { get; } = [];


        public bool ShouldSendNotification => MessagesAggregationTimeSec > 0 && _nextSendMessageTime <= DateTime.UtcNow;


        public SlackDestination(SlackAddRequest add) : base(add)
        {
            WebhookUrl = add.WebhookUrl;
            SendMessages = DefaultSendMessages;
            MessagesAggregationTimeSec = add.MessagesAggregationTimeSec;
        }

        internal SlackDestination(SlackDestinationEntity entity) : base(entity)
        {
            WebhookUrl = entity.WebhookUrl;
            SendMessages = entity.SendMessages;
            MessagesAggregationTimeSec = entity.MessagesAggregationTimeSec;
        }


        protected override void ApplyUpdate(SlackDestinationUpdate update)
        {
            WebhookUrl = update.WebhookUrl ?? WebhookUrl;
            SendMessages = update.SendMessages ?? SendMessages;
            MessagesAggregationTimeSec = update.MessagesAggregationTimeSec ?? MessagesAggregationTimeSec;
        }

        public override SlackDestinationEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.WebhookUrl = WebhookUrl;
            entity.SendMessages = SendMessages;
            entity.MessagesAggregationTimeSec = MessagesAggregationTimeSec;

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
