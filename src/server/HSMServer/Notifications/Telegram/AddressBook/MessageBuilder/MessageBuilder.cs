using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Notification.Settings;
using HSMServer.Notifications.Telegram.AddressBook.MessageBuilder;
using System;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class MessageBuilder
    {
        private readonly CDict<CTupleDict<CHash>> _messageTree = new();
        private readonly PathCompressor _compressor = new();

        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var newStatus = sensor.ValidationResult.Result.ToStatusIcon();
            var comment = sensor.ValidationResult.Message;

            var id = sensor.Id;
            var branch = _messageTree[sensor.RootProductName];

            if (_compressor.TryGetOrAdd(sensor, out var key))
            {
                newStatus = $"{key.oldStatus}->{newStatus}";

                branch[key].Remove(id);

                if (branch[key].IsEmpty)
                    branch.TryRemove(key, out _);
            }

            var newKey = (newStatus, comment);

            branch[newKey].Add(id);
            _compressor[id] = newKey;
        }

        internal string GetAggregateMessage(int notificationsDelay)
        {
            var builder = new StringBuilder(1 << 10);

            foreach (var (product, changePaths) in _messageTree)
            {
                foreach (((var status, var comment), var sensors) in changePaths)
                {
                    foreach (var path in _compressor.GetGroupedPaths(sensors))
                    {
                        BuildMessage(builder, product, status, comment, path);
                    }

                    sensors.Clear();
                }

                changePaths.Clear();
                builder.AppendLine();
            }

            ExpectedSendingTime = GetNextNotificationTime(notificationsDelay);

            _compressor.Clear();
            _messageTree.Clear();

            return builder.ToString();
        }

        public static string GetSingleMessage(BaseSensorModel sensor)
        {
            var builder = new StringBuilder(1 << 5);

            var comment = sensor.ValidationResult.Message;
            var result = sensor.ValidationResult.Result.ToStatusIcon();
            var product = sensor.RootProductName;
            var path = sensor.Path;

            BuildMessage(builder, product, result, comment, path);

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

        private static DateTime GetNextNotificationTime(int notificationsDelay)
        {
            var ticks = DateTime.MinValue.AddSeconds(notificationsDelay).Ticks;

            if (ticks == 0L)
                return DateTime.UtcNow;

            var start = DateTime.UtcNow.Ticks / ticks * ticks;

            return new DateTime(start + ticks);
        }
    }
}
