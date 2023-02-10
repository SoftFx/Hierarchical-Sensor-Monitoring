using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace HSMServer.Notifications
{ 
    internal static class NotificationExtensions
    {
        private static readonly string[] _specialSymbolsMarkdownV2 = new[]
            {"_", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!", "*"};

        private static string[] _escapedSymbols;


        static NotificationExtensions()
        {
            BuildEscapedSymbols();
        }


        public static string EscapeMarkdownV2(this string message)
        {
            for (int i = 0; i < _escapedSymbols.Length; i++)
                message = message.Replace(_specialSymbolsMarkdownV2[i], _escapedSymbols[i]);

            return message;
        }


        private static void BuildEscapedSymbols()
        {
            _escapedSymbols = new string[_specialSymbolsMarkdownV2.Length];

            for (int i = 0; i < _escapedSymbols.Length; i++)
                _escapedSymbols[i] = $"\\{_specialSymbolsMarkdownV2[i]}";
        }
    }
    
    
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

        private ConcurrentDictionary<string, int> NodeSensorsCount = new();

        internal DateTime LastSentTime { get; private set; } = DateTime.UtcNow;


        internal void AddMessage(BaseSensorModel sensor)
        {
            var product = sensor.RootProductName;
            var nodePath = sensor.ParentProduct.Path ?? string.Empty;

            var pathDict = _messageTree[product][nodePath];
            
            NodeSensorsCount[nodePath] = sensor.ParentProduct.Sensors.Count;
            
            var messages = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            pathDict[result][messages].Push(sensor.DisplayName);
        }

        internal string GetAggregateMessage()
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
                            GetSingleLine(builder, productName, result, message, sensors.GenerateOutputSensors(NodeSensorsCount[nodePath]));
                        }
                    }
                    
                    if(!string.IsNullOrEmpty(nodePath))
                        builder.Append($" at {nodePath}".EscapeMarkdownV2());
                    
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
            var builder = new StringBuilder(1 << 2);
            
            var product = sensor.RootProductName;
            var nodePath = sensor.ParentProduct.Path ?? string.Empty;
            var message = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";
            
            GetSingleLine(builder, product, result, message, sensor.DisplayName, nodePath);

            return builder.ToString();
        }

        private static void GetSingleLine(StringBuilder builder, string productName,string result, string message, string sensors, string nodePath = null)
        {
            builder.Append($"{productName.EscapeMarkdownV2()}: ");
            
            builder.Append($"*{result.EscapeMarkdownV2()}* ");

            if (!string.IsNullOrEmpty(message))
                builder.Append($"* {('(' + message + ')').EscapeMarkdownV2()}* ");

            builder.Append(" -> ".EscapeMarkdownV2()).Append(sensors.EscapeMarkdownV2());
            
            if(!string.IsNullOrEmpty(nodePath))
                builder.Append($" at {nodePath}".EscapeMarkdownV2());
        }
    }
}
