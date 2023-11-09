using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Notifications.Telegram.AddressBook.MessageBuilder;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class MessageBuilder
    {
        private readonly CDict<CDict<ConcurrentDictionary<Guid, AlertResult>>> _alertsTree = new(); //template -> policyId -> Message as string -> AlertResult
        private readonly AlertsGrouper _grouper = new();

        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(AlertResult alert)
        {
            var mapComment = alert.IsStatusIsChangeResult ? string.Empty : alert.LastComment;
            var branch = _alertsTree[alert.Template][mapComment];

            if (branch.TryGetValue(alert.PolicyId, out var policy))
                policy.TryAddResult(alert);
            else
                branch.TryAdd(alert.PolicyId, alert with { });
        }

        internal string GetAggregateMessage(int delay)
        {
            foreach (var (_, sensorAlerts) in _alertsTree)
            {
                foreach (var (_, alerts) in sensorAlerts)
                {
                    foreach (var (_, aggrAlert) in alerts)
                        _grouper.ApplyToGroup(aggrAlert);

                    alerts.Clear();
                }

                sensorAlerts.Clear();
            }

            _alertsTree.Clear();

            var builder = new StringBuilder(1 << 10);

            foreach (var line in _grouper.GetGroups())
                builder.AppendLine(line);

            ExpectedSendingTime = DateTime.UtcNow.Ceil(TimeSpan.FromSeconds(delay));

            return builder.ToString();
        }
    }
}