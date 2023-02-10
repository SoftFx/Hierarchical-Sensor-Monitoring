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

        internal string GenerateOutputSensors(int nodeSensorsCount)
        {
            var sensors = string.Join(", ", this);
            if (TotalCount > MaxSensorsCount)
                sensors = $"{sensors} ... ({TotalCount}/{nodeSensorsCount})";

            Clear();

            return sensors;
        }
    }

    internal sealed class CDict<T> : ConcurrentDictionary<string, T> where T : new()
    {
        public new T this[string key] => GetOrAdd(key);

        public int NodeSensorsCount { get; set; }
        
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

            if (pathDict.NodeSensorsCount == 0) 
                pathDict.NodeSensorsCount = GetAllSensorsFromSpecificPath(sensor.ParentProduct, nodePath);
            
            
            var messages = sensor.ValidationResult.Message;
            var result = $"{sensor.ValidationResult.Result}";

            pathDict[result][messages].Push(sensor.DisplayName);
        }

        internal int GetAllSensorsFromSpecificPath(ProductModel productModel, string path)
        {
            if (productModel.SubProducts.Count == 0 && productModel.Path != path)
                return -1;

            foreach (var (_, subProduct) in productModel.SubProducts)
            {
                GetAllSensorsFromSpecificPath(subProduct, path);
            }

            return productModel.Sensors.Count;
        }

        internal string GetAggregateMessage()
        {
            var builder = new StringBuilder(1 << 8);

            foreach (var (productName, nodes) in _messageTree)
            {
                foreach (var (nodepath, results) in nodes)
                {
                    if (!nodes.IsEmpty) 
                        builder.Append($"{productName}: ");

                    foreach (var (result, messages) in results)
                    {
                        foreach (var (message, sensors) in messages)
                        {
                            builder.Append($"<b>{result}</b> ");

                            if (!string.IsNullOrEmpty(message))
                                builder.Append($"<b>({message})</b> ");

                            builder.Append(" -> ").Append(sensors.GenerateOutputSensors(results.NodeSensorsCount));
                        }
                    }

                    builder.Append($" at {(string.IsNullOrEmpty(nodepath) ? "/" : $"{nodepath}")} ");
                    
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
