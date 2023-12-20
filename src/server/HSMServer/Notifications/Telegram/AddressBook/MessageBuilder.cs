using HSMCommon.Collections;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal interface IMessageBuilder
    {
        void AddMessage(AlertResult alert);
    }


    internal sealed class MessageBuilder : IMessageBuilder
    {
        private readonly CDict<CDict<ConcurrentDictionary<Guid, AlertResult>>> _alertsTree = new(); //template -> policyId -> Message as string -> AlertResult
        private readonly AlertsGrouper _grouper = new();


        public void AddMessage(AlertResult alert)
        {
            var mapComment = alert.IsStatusIsChangeResult ? string.Empty : alert.LastComment;
            var branch = _alertsTree[alert.Template][mapComment];

            if (branch.TryGetValue(alert.PolicyId, out var policy))
                policy.TryAddResult(alert);
            else
                branch.TryAdd(alert.PolicyId, alert with { });
        }

        internal string GetAggregateMessage()
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

            return builder.ToString();
        }
    }
}