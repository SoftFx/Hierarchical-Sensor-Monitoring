using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;
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
        private const int MaxSensorMessages = 10;


        private readonly CDict<CDict<CDict<CDict<CQueue>>>> _messageTree = new();

        
        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var product = sensor.RootProductName;
            var nodePath = sensor.ParentProduct.Path ?? "";

            var pathDict = _messageTree[product][nodePath];

            var messages = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            pathDict[result][messages].Push(sensor.DisplayName);
        }

        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 8);

            foreach (var (productName, nodes) in _messageTree)
            {
                builder.AppendLine(productName);
                foreach (var (nodepath, results) in nodes)
                {
                    builder.Append($"   {(string.IsNullOrEmpty(nodepath) ? "/" : $"{nodepath}")}: ");
                    foreach (var (result, messages) in results)
                    {
                        foreach (var (message, sensors) in messages)
                        {
                            builder.Append(GenerateOutputSensors(sensors));
                            builder.Append($" -> {result} {(result.Equals("Ok") ? "" : $"({message})")}");
                            
                            sensors.Clear();
                        }
                    }

                    builder.AppendLine();
                }

                builder.AppendLine();
                builder.AppendLine();
            }

            Reset();

            return builder.ToString();
        }

        private void Reset()
        {
            _messageTree.Clear();
            LastSentTime = DateTime.UtcNow;
        }

        private static string GenerateOutputSensors(CQueue sensors) =>
            sensors.Count > MaxSensorMessages
            ? string.Join(", ", sensors.Take(MaxSensorMessages)) + $" ... (and other {sensors.Count - MaxSensorMessages})"
            : string.Join(", ", sensors);
    }
}
