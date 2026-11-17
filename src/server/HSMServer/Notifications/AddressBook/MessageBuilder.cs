using HSMServer.Core.Model.Policies;
using System;
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
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(MessageBuilder));

        private readonly AlertsGrouper _grouper = new();
        private readonly object _treeLock = new();

        // template -> rendered comment -> policyId -> alert. Alerts arrive on the thread that raises
        // them while the periodic flush drains the buffer on another one, so the tree is read and
        // changed only under _treeLock. A drain takes the whole tree and leaves an empty one: an
        // alert is either in the tree that was taken or in the new one, never in a branch that the
        // drain has already read and is about to drop (#1778).
        private Dictionary<string, Dictionary<string, Dictionary<Guid, AlertResult>>> _alertsTree = new();


        public void AddMessage(AlertResult alert)
        {
            var mapComment = alert.IsStatusIsChangeResult ? string.Empty : alert.LastComment;

            lock (_treeLock)
            {
                if (!_alertsTree.TryGetValue(alert.Template, out var comments))
                    _alertsTree[alert.Template] = comments = new();

                if (!comments.TryGetValue(mapComment, out var branch))
                    comments[mapComment] = branch = new();

                if (branch.TryGetValue(alert.PolicyId, out var policy))
                    policy.TryAddResult(alert);
                else
                    branch.Add(alert.PolicyId, alert with { });
            }
        }

        // Always empties the buffer. An alert left behind by an exception would fail again on
        // every aggregation period, and the chat would get no message until the server restarts.
        internal string GetAggregateMessage()
        {
            var ungrouped = new List<AlertResult>(); // sent as they are, after the groups
            Exception firstError = null;

            Dictionary<string, Dictionary<string, Dictionary<Guid, AlertResult>>> taken;

            // From here on the taken tree belongs to the drain alone: AddMessage finishes its
            // change under the lock and keeps no reference to a branch after it.
            lock (_treeLock)
            {
                taken = _alertsTree;
                _alertsTree = new();
            }

            foreach (var (_, sensorAlerts) in taken)
                foreach (var (_, alerts) in sensorAlerts)
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