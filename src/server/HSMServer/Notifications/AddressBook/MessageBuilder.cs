using HSMCommon.Collections;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Notifications.AddressBook
{
    internal interface IMessageBuilder
    {
        void AddMessage(AlertResult alert);
    }


    internal sealed class MessageBuilder : IMessageBuilder
    {
        private readonly CStringDict<CStringDict<ConcurrentDictionary<Guid, AlertResult>>> _alertsTree = new(); //template -> policyId -> Message as string -> AlertResult
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(MessageBuilder));

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

        // Always empties the buffer. An alert left behind by an exception would fail again on
        // every aggregation period, and the chat would get no message until the server restarts.
        internal string GetAggregateMessage()
        {
            var ungrouped = new List<AlertResult>(); // sent as they are, after the groups
            Exception firstError = null;

            foreach (var (_, sensorAlerts) in _alertsTree)
            {
                foreach (var (_, alerts) in sensorAlerts)
                {
                    foreach (var (_, aggrAlert) in alerts)
                        try
                        {
                            _grouper.ApplyToGroup(aggrAlert);
                        }
                        catch (Exception ex)
                        {
                            firstError ??= ex;
                            ungrouped.Add(aggrAlert);
                        }

                    alerts.Clear();
                }

                sensorAlerts.Clear();
            }

            _alertsTree.Clear();

            var builder = new StringBuilder(1 << 10);

            foreach (var group in _grouper.TakeGroups())
                try
                {
                    builder.AppendLine(group.ToString());
                }
                catch (Exception ex)
                {
                    // The alerts merged into the group are represented by its first alert.
                    firstError ??= ex;
                    ungrouped.Add(group.BaseAlert);
                }

            var dropped = 0;

            foreach (var alert in ungrouped)
                try
                {
                    builder.AppendLine(alert.ToString());
                }
                catch (Exception ex)
                {
                    dropped++;
                    _logger.Error($"Alert of policy {alert.PolicyId} cannot be rendered and is dropped from the message: {ex}");
                }

            // One record per message, not per alert: a fault that hits every alert must not flood the log.
            if (ungrouped.Count > 0)
                _logger.Error($"{ungrouped.Count} alert(s) could not be aggregated: {ungrouped.Count - dropped} sent ungrouped, {dropped} dropped; first policy {ungrouped[0].PolicyId}: {firstError}");

            return builder.ToString();
        }
    }
}