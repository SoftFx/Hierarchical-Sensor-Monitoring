using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Concurrent;
using System.Text;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class QueueOverflowSensor : IntBarPublicSensor
    {
        private readonly ConcurrentDictionary<string, long> _queuesOverflowInfo = new ConcurrentDictionary<string, long>();


        public QueueOverflowSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(string queueName, int value)
        {
            if (!_queuesOverflowInfo.ContainsKey(queueName))
                _queuesOverflowInfo.TryAdd(queueName, value);
            else
                _queuesOverflowInfo[queueName] += value;
        }


        protected override SensorValueBase BuildSensorValue()
        {
            var buildedValue = base.BuildSensorValue();

            buildedValue.Comment = GetQueueStats();

            return buildedValue;
        }


        private string GetQueueStats()
        {
            var sb = new StringBuilder(1 << 10);

            foreach (var pair in _queuesOverflowInfo)
                if (pair.Value > 0)
                    sb.AppendLine($"{pair.Key}: {pair.Value}");

            _queuesOverflowInfo.Clear();

            return sb.ToString();
        }
    }
}