using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Core.Notifications
{
    internal sealed class MessageBuilder
    {
        private readonly ConcurrentDictionary<string, List<MessageInfo>> _messages = new();


        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor, string productId)
        {
            if (!_messages.ContainsKey(productId))
                _messages[productId] = new List<MessageInfo>();

            _messages[productId].Add(GenerateMessageInfo(sensor));
        }

        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 6);

            foreach (var (_, messages) in _messages)
            {
                var orderdMessaged = messages.OrderBy(m => m.SensorPath).ThenBy(m => m.SensorValueTime);
                var productName = messages[0].ProductName;

                builder.AppendLine(productName);
                foreach (var message in orderdMessaged)
                    builder.AppendLine(message.Message);

                builder.AppendLine();
            }

            Reset();

            return builder.ToString();
        }

        internal static string GetSingleMessage(BaseSensorModel sensor)
        {
            var messageInfo = GenerateMessageInfo(sensor);
            var builder = new StringBuilder(1 << 5);

            builder.AppendLine(messageInfo.ProductName);
            builder.Append(messageInfo.Message);

            return builder.ToString();
        }

        private void Reset()
        {
            _messages.Clear();

            LastSentTime = DateTime.UtcNow;
        }

        private static MessageInfo GenerateMessageInfo(BaseSensorModel sensor)
        {
            var builder = new StringBuilder(1 << 2);

            builder.Append($"    {sensor.Path}: {sensor.ValidationResult.Result}");
            if (!sensor.ValidationResult.IsSuccess)
                builder.Append($" ({sensor.ValidationResult.Message})");

            return new()
            {
                SensorValueTime = sensor.LastValue.Time,
                SensorPath = sensor.Path,
                ProductName = sensor.ProductName,
                Message = builder.ToString(),
            };
        }


        private readonly struct MessageInfo
        {
            internal DateTime SensorValueTime { get; init; }

            internal string SensorPath { get; init; }

            internal string ProductName { get; init; }

            internal string Message { get; init; }
        }
    }
}
