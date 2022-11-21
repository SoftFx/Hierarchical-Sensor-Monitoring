using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class MessageBuilder
    {
        private const int MaxSensorMessages = 5;

        private readonly ConcurrentDictionary<string, Dictionary<string, MessagesQueue>> _messages = new();


        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var productId = sensor.RootProductId;

            if (!_messages.ContainsKey(productId))
                _messages[productId] = new Dictionary<string, MessagesQueue>();

            var productMessages = _messages[productId];

            if (!productMessages.ContainsKey(sensor.Path))
                productMessages.Add(sensor.Path, new MessagesQueue());

            productMessages[sensor.Path].AddMessage(GenerateMessageInfo(sensor));
        }

        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 6);

            foreach (var (_, messages) in _messages)
            {
                var productName = messages?.FirstOrDefault().Value.Messages?.FirstOrDefault().ProductName;
                builder.AppendLine(productName);

                foreach (var (_, messagesQueue) in messages)
                {
                    if (messagesQueue.AllMessagesCount > MaxSensorMessages)
                        builder.AppendLine($"    ... ({messagesQueue.AllMessagesCount - MaxSensorMessages} other message(s))");

                    foreach (var message in messagesQueue.Messages.OrderBy(m => m.SensorValueTime))
                        builder.AppendLine(message.Message);
                }

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
                SensorValueTime = sensor.LastValue?.Time ?? DateTime.MinValue,
                SensorPath = sensor.Path,
                ProductName = sensor.RootProductName,
                Message = builder.ToString(),
            };
        }


        private sealed record MessagesQueue
        {
            internal Queue<MessageInfo> Messages { get; } = new();

            internal int AllMessagesCount { get; private set; }


            internal void AddMessage(MessageInfo message)
            {
                AllMessagesCount++;

                Messages.Enqueue(message);

                if (Messages.Count > MaxSensorMessages)
                    Messages.Dequeue();
            }
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
