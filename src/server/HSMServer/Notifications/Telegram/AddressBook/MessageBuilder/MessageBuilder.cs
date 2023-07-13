using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Notification.Settings;
using HSMServer.Notifications.Telegram.AddressBook.MessageBuilder;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class MessageBuilder
    {
        private readonly CDict<ConcurrentDictionary<Guid, AlertResult>> _alertsTree = new(); //template -> policyId -> AlertResult
        private readonly AlertsCompressor _compressor = new();

        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(AlertResult alert)
        {
            var branch = _alertsTree[alert.Template];

            if (branch.TryGetValue(alert.PolicyId, out var policy))
                policy.TryAddResult(alert);
            else
                branch.TryAdd(alert.PolicyId, alert);
        }

        internal string GetAggregateMessage(int delay)
        {
            foreach (var (_, alerts) in _alertsTree)
            {
                foreach (var (_, aggrAlert) in alerts)
                    _compressor.ApplyToGroup(aggrAlert);

                alerts.Clear();
            }

            _alertsTree.Clear();

            var builder = new StringBuilder(1 << 10);

            foreach (var line in _compressor.GetGroups())
                builder.AppendLine(line);

            ExpectedSendingTime = DateTime.UtcNow.Ceil(TimeSpan.FromSeconds(delay));

            return builder.ToString();
        }
    }
}