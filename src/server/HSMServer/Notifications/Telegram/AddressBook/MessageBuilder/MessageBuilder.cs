﻿using HSMServer.Core.Model;
using HSMServer.Notification.Settings;
using HSMServer.Notifications.Telegram.AddressBook.MessageBuilder;
using System;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class MessageBuilder
    {
        private readonly CDict<CTupleDict<CGuidHash>> _messageTree = new();
        private readonly PathCompressor _compressor = new();

        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor, SensorStatus firstStatus)
        {
            var newStatus = sensor.Status.Icon;
            var comment = sensor.Status.Message;

            var id = sensor.Id;
            var branch = _messageTree[sensor.RootProductName];

            if (_compressor.TryGetOrAdd(sensor, firstStatus, out var key))
            {
                newStatus = $"{key.oldStatus}->{newStatus}";

                branch[key].Remove(id);
                branch.RemoveEmptyBranch(key);
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
                foreach ((var changeStatusPath, var sensors) in changePaths)
                {
                    (var status, var comment) = changeStatusPath;

                    foreach (var path in _compressor.GetGroupedPaths(sensors))
                    {
                        BuildMessage(builder, product, status, comment, path);
                    }

                    changePaths.RemoveEmptyBranch(changeStatusPath);
                }

                builder.AppendLine();

                _messageTree.RemoveEmptyBranch(product);
            }

            ExpectedSendingTime = GetNextNotificationTime(notificationsDelay);

            return builder.ToString();
        }

        public static string GetSingleMessage(BaseSensorModel sensor)
        {
            var builder = new StringBuilder(1 << 5);

            var comment = sensor.Status.Message;
            var result = sensor.Status.Icon;
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
