using HSMServer.Core.Model;
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

        internal string GenerateOutputSensors()
        {
            var sensors = string.Join(", ", this);
            if (TotalCount > MaxSensorsCount) 
                sensors = $"{sensors} ... (and other {TotalCount - MaxSensorsCount})";
            
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
                if(!nodes.IsEmpty)
                    builder.AppendLine(productName);
                
                foreach (var (nodepath, results) in nodes)
                {
                    builder.Append($"   {(string.IsNullOrEmpty(nodepath) ? "/" : $"{nodepath}")}: ");
                    foreach (var (result, messages) in results)
                    {
                        foreach (var (message, sensors) in messages)
                        {
                            builder.Append(sensors.GenerateOutputSensors())
                                   .Append($" -> {result} ");
                            if (!string.IsNullOrEmpty(message)) 
                                builder.Append($"({message})");
                        }
                    }

                    builder.AppendLine();
                }

                nodes.Clear();
                
                builder.AppendLine();
            }

            LastSentTime = DateTime.UtcNow;

            return builder.ToString();
        }

        public static string GetSingleMessage(BaseSensorModel sensor)
        {
            var product = sensor.RootProductName;
            var nodePath = sensor.ParentProduct.Path ?? string.Empty;
            
            var message = sensor.ValidationResult.Message;
            var result = sensor.ValidationResult.Result;
            var resultMessage = string.IsNullOrEmpty(message) ? $"{result}" : $"{result} ({message})";
            
            return $"{product}{Environment.NewLine}   {(string.IsNullOrEmpty(nodePath) ? "/" : $"{nodePath}")}: {sensor.DisplayName} -> {resultMessage}";
        }
    }
}
