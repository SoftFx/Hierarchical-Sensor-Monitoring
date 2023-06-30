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
        //private readonly CDict<CTupleDict<CGuidHash>> _messageTree = new();
        private readonly CDict<ConcurrentDictionary<Guid, AlertResult>> _alertsTree = new();
        private readonly PathCompressor _compressor = new();

        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(AlertResult alert)
        {
            var branch = _alertsTree[alert.Template];

            if (branch.TryGetValue(alert.PolicyId, out var policy))
                policy.TryAddResult(alert);
            else
                branch.TryAdd(alert.PolicyId, alert);


            //var newStatus = sensor.Status.Icon;
            //var comment = sensor.Status.Message;

            //var id = sensor.Id;
            //var branch = _messageTree[sensor.RootProductName];

            //if (_compressor.TryGetOrAdd(sensor, firstStatus, out var key))
            //{
            //    newStatus = $"{key.oldStatus}->{newStatus}";

            //    branch[key].Remove(id);
            //    branch.RemoveEmptyBranch(key);
            //}

            //var newKey = (newStatus, comment);

            //branch[newKey].Add(id);
            //_compressor[id] = newKey;
        }

        internal string GetAggregateMessage(int notificationsDelay)
        {
            var builder = new StringBuilder(1 << 10);

            foreach (var (_, alerts) in _alertsTree)
            {
                foreach (var (_, aggrAlert) in alerts)
                    builder.AppendLine(aggrAlert.ToString());

                alerts.Clear();
            }

            _alertsTree.Clear();

            //foreach (var (product, changePaths) in _messageTree)
            //{
            //    foreach ((var changeStatusPath, var sensors) in changePaths)
            //    {
            //        (var status, var comment) = changeStatusPath;

            //        foreach (var path in _compressor.GetGroupedPaths(sensors))
            //        {
            //            BuildMessage(builder, product, status, comment, path);
            //        }

            //        changePaths.RemoveEmptyBranch(changeStatusPath);
            //    }

            //    builder.AppendLine();

            //    _messageTree.RemoveEmptyBranch(product);
            //}

            ExpectedSendingTime = DateTime.UtcNow.Floor(TimeSpan.FromSeconds(notificationsDelay));

            return builder.ToString();
        }

        private static void BuildMessage(StringBuilder builder, string productName, string statusPath, string comment, string path)
        {
            productName = $"[{productName}]".EscapeMarkdownV2();
            statusPath = statusPath.EscapeMarkdownV2();
            path = $"{path}".EscapeMarkdownV2();

            if (!string.IsNullOrEmpty(comment))
                comment = $" = {comment}".EscapeMarkdownV2();

            builder.AppendLine($"{statusPath} {productName}{path}{comment}");
        }
    }
}