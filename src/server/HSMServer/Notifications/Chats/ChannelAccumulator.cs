using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Notifications.AddressBook;
using System;
using System.Collections.Generic;

namespace HSMServer.Notifications.Chats
{
    // Per-channel aggregation state. A unified Chat can deliver through several channels
    // (Telegram, Slack, ...). Each channel must accumulate and flush independently — sharing a
    // single MessageBuilder/ScheduleBuilder/next-send timer across channels doubles the buffered
    // alert in both builders, and the channel that flushes first drains the buffer + bumps the
    // timer, so every later channel is skipped.
    internal sealed class ChannelAccumulator
    {
        public ScheduleBuilder ScheduleBuilder { get; } = new();

        public MessageBuilder MessageBuilder { get; } = new();


        private DateTime _nextSendMessageTime;


        public void AddMessage(AlertResult alert, bool scheduled)
        {
            if (scheduled)
                ScheduleBuilder.AddMessage(alert);
            else
                MessageBuilder.AddMessage(alert);
        }

        public bool ShouldSend(int aggregationTimeSec) =>
            aggregationTimeSec > 0 && _nextSendMessageTime <= DateTime.UtcNow;

        public IEnumerable<string> GetNotifications(int aggregationTimeSec)
        {
            try
            {
                foreach (var report in ScheduleBuilder.GetReports())
                    yield return report;

                yield return MessageBuilder.GetAggregateMessage();
            }
            finally
            {
                _nextSendMessageTime = DateTime.UtcNow.Ceil(TimeSpan.FromSeconds(aggregationTimeSec));
            }
        }
    }
}
