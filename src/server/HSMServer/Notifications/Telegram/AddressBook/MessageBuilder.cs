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
        private const int MaxSensorMessages = 20;

        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, List<MessageInfo>>> _messages = new();

        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var productId = sensor.RootProductId;

            if (!_messages.ContainsKey(productId))
                _messages[productId] = new ConcurrentDictionary<string, List<MessageInfo>>();

            var nodePath = GetNodePath(sensor.Path);
            if (!_messages[productId].ContainsKey(nodePath))
                _messages[productId].TryAdd(nodePath, new List<MessageInfo>());

            _messages[productId][nodePath].Add(GenerateMessageInfo(sensor));
        }

        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 6);
            var response = new Dictionary<string, List<string>>(1 << 8);

            foreach (var (_, messages) in _messages)
            {
                var productName = messages?.FirstOrDefault().Value?.FirstOrDefault().ProductName;
                builder.AppendLine(productName);
             
                foreach (var (nodepath, notifications) in messages)
                {
                    foreach (var notification in notifications)
                    {
                        if (!response.ContainsKey(notification.Message))
                        {
                            response.TryAdd(notification.Message, new List<string>(1 << 4));
                        }

                        response[notification.Message].Add(notification.SensorDisplayName);
                    }

                    foreach (var (state, sensors) in response)
                    {
                        var outputSensors = GenerateOutputSensors(sensors);
                        
                        builder.Append($"    {(string.IsNullOrEmpty(nodepath) ? "/" : $"{nodepath}")}: {outputSensors} -> {state}");
                    }

                    builder.AppendLine();
                    response.Clear();
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
 
            builder.Append($"{sensor.ValidationResult.Result}");
            if (!sensor.ValidationResult.IsSuccess)
                builder.Append($" ({sensor.ValidationResult.Message})");

            return new()
            {
                SensorValueTime = sensor.LastValue?.Time ?? DateTime.MinValue,
                SensorPath = sensor.Path,
                ProductName = sensor.RootProductName,
                Message = builder.ToString(),
                SensorDisplayName = sensor.DisplayName
            };
        }

        private static string GenerateOutputSensors(List<string> sensors) =>
            sensors.Count > MaxSensorMessages
            ? string.Join(", ", sensors.Take(MaxSensorMessages)) + $" ... (and other {sensors.Count - MaxSensorMessages})"
            : string.Join(", ", sensors);

        private static string GetNodePath(string sensorPath) => sensorPath.Remove(sensorPath.LastIndexOf('/'));


        private readonly struct MessageInfo
        {
            internal DateTime SensorValueTime { get; init; }

            internal string SensorPath { get; init; }

            internal string ProductName { get; init; }

            internal string Message { get; init; }
            
            internal string SensorDisplayName { get; init; }
        }
    }
}
