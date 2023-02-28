﻿using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class CQueue : ConcurrentQueue<string>
    {
        private const int MaxSensorsCount = 10;

        internal long TotalCount { get; private set; }


        internal void Push(string message)
        {
            TotalCount++;

            while (Count > MaxSensorsCount)
                TryDequeue(out _);

            Enqueue(message);
        }


        internal new void Clear()
        {
            base.Clear();

            TotalCount = 0L;
        }

        internal string GenerateOutputSensors(int nodeSensorsCount)
        {
            var sensors = string.Join(", ", this);
            if (TotalCount > MaxSensorsCount)
                sensors = $"{sensors} ... ({TotalCount}/{nodeSensorsCount})";

            Clear();

            return sensors;
        }
    }

    internal sealed class CDict<T> : ConcurrentDictionary<string, T> where T : class, new()
    {
        public new T this[string key] => GetOrAdd(key);


        internal T GetOrAdd(string key)
        {
            if (!TryGetValue(key, out T value))
            {
                base[key] = new T();

                return base[key];
            }

            return value;
        }
    }

    internal sealed class MessageBuilder
    {
        private readonly CDict<CDict<CDict<CDict<CQueue>>>> _messageTree = new();

        private readonly ConcurrentDictionary<string, int> NodeSensorsCount = new();


        internal DateTime ExpectedSendingTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var product = sensor.RootProductName;
            var nodePath = sensor?.ParentProduct?.Path ?? string.Empty;

            var pathDict = _messageTree[product][nodePath];

            NodeSensorsCount[nodePath] = sensor.ParentProduct?.Sensors?.Count ?? 0;

            var messages = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            pathDict[result][messages].Push(sensor.DisplayName);
        }

        internal string GetAggregateMessage(int notificationMessageDelay)
        {
            var builder = new StringBuilder(1 << 8);

            foreach (var (productName, nodes) in _messageTree)
            {
                foreach (var (nodePath, results) in nodes)
                {
                    foreach (var (result, messages) in results)
                    {
                        foreach (var (message, sensors) in messages)
                        {
                            BuildMessage(builder, productName, result, message, sensors.GenerateOutputSensors(NodeSensorsCount[nodePath]), nodePath);
                        }
                    }

                    builder.AppendLine();
                }

                nodes.Clear();

                builder.AppendLine();
            }

            ExpectedSendingTime = GetNextNotificationTime(notificationMessageDelay);

            NodeSensorsCount.Clear();

            return builder.ToString();
        }

        public static string GetSingleMessage(BaseSensorModel sensor)
        {
            var builder = new StringBuilder(1 << 2);

            var product = sensor.RootProductName;
            var nodePath = sensor?.ParentProduct?.Path ?? string.Empty;
            var message = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            BuildMessage(builder, product, result, message, sensor.DisplayName, nodePath);

            return builder.ToString();
        }

        private static void BuildMessage(StringBuilder builder, string productName, string result, string message, string sensors, string nodePath)
        {
            productName = productName.EscapeMarkdownV2();
            result = result.EscapeMarkdownV2();
            sensors = $"-> {sensors}".EscapeMarkdownV2();

            if (!string.IsNullOrEmpty(nodePath))
                nodePath = $" at {nodePath}".EscapeMarkdownV2();

            if (!string.IsNullOrEmpty(message))
                message = $" ({message})".EscapeMarkdownV2();

            builder.AppendLine($"{productName}: *{result}{message}* {sensors}{nodePath}");
        }
        
        private static DateTime GetNextNotificationTime(int notificationsDelay)
        {
            var ticks = DateTime.MinValue.AddSeconds(notificationsDelay).Ticks;
            var start = DateTime.UtcNow.Ticks / ticks * ticks;

            return new DateTime(start + ticks);
        }
    }
}
