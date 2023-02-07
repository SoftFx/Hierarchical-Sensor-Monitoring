using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HSMServer.Notifications
{
    internal sealed class CQueue : ConcurrentQueue<string>
    {
        private const int MaxMessagesCount = 20;

        internal long TotalCount { get; private set; }


        internal void Push(string message)
        {
            TotalCount++;

            while (Count > MaxMessagesCount)
                TryDequeue(out _);

            Enqueue(message);
        }


        internal new void Clear()
        {
            base.Clear();

            TotalCount = 0L;
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
        private const int MaxSensorMessages = 20;

        //private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, List<MessageInfo>>> _messages = new();

        private readonly CDict<CDict<CDict<CDict<CQueue>>>> _messageTree = new();

        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            //var productId = sensor.RootProductId;

            //if (!_messages.ContainsKey(productId))
            //    _messages[productId] = new ConcurrentDictionary<string, List<MessageInfo>>();

            var product = sensor.ParentProduct.DisplayName;
            var nodePath = sensor.ParentProduct.Path;

            //var nodePath = GetNodePath(sensor.Path);
            //if (!_messages[productId].ContainsKey(nodePath))
            //    _messages[productId].TryAdd(nodePath, new List<MessageInfo>());


            var pathDict = _messageTree[product][nodePath];

            var messages = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            pathDict[result][messages].Push(sensor.DisplayName);
            //_messages[productId][nodePath].Add(GenerateMessageInfo(sensor));
        }

        //private void PushMessage(CDict<CDict<CQueue>> pathDict, BaseSensorModel model)
        //{

        //}


        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 6);
            var response = new Dictionary<string, List<string>>(1 << 8);

            foreach (var (_, messages) in _messageTree)
            {
                var productName = messages?.FirstOrDefault().Value?.FirstOrDefault().ProductName;
                builder.AppendLine(productName);

                foreach (var (nodepath, messagesQueue) in messages)
                {
                    foreach (var messageInfo in messagesQueue)
                    {
                        if (!response.ContainsKey(messageInfo.Message))
                        {
                            response.TryAdd(messageInfo.Message, new List<string>(1 << 4));
                        }

                        response[messageInfo.Message].Add(messageInfo.SensorDisplayName);
                    }

                    foreach (var (message, output) in response)
                    {
                        var outputSensors = GenerateOutputSensors(output);

                        builder.Append($"    {(string.IsNullOrEmpty(nodepath) ? "/" : $"{nodepath}")}: {outputSensors} -> {message}");
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

        //private static MessageInfo GenerateMessageInfo(BaseSensorModel sensor)
        //{
        //    var builder = new StringBuilder(1 << 2);

        //    builder.Append($"{sensor.ValidationResult.Result}");
        //    if (!sensor.ValidationResult.IsSuccess)
        //        builder.Append($" ({sensor.ValidationResult.Message})");

        //    return new()
        //    {
        //        SensorValueTime = sensor.LastValue?.Time ?? DateTime.MinValue,
        //        SensorPath = sensor.Path,
        //        ProductName = sensor.RootProductName,
        //        Message = builder.ToString(),
        //        SensorDisplayName = sensor.DisplayName
        //    };
        //}

        private static string GenerateOutputSensors(List<string> sensors) =>
            sensors.Count > MaxSensorMessages
            ? string.Join(", ", sensors.Take(MaxSensorMessages)) + $" ... (and other {sensors.Count - MaxSensorMessages})"
            : string.Join(", ", sensors);

        //private static string GetNodePath(string sensorPath) => sensorPath.Remove(sensorPath.LastIndexOf('/'));


        //private readonly struct MessageInfo
        //{
        //    //internal DateTime SensorValueTime { get; init; }

        //    //internal string SensorPath { get; init; }

        //    //internal string ProductName { get; init; }

        //    internal string Message { get; init; }

        //    internal string SensorName { get; init; }
        //}
    }
}
