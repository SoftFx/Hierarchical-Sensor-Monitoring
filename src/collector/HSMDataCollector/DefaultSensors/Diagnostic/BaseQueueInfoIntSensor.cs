using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Concurrent;
using System.Text;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal abstract class BaseQueueInfoIntSensor : IntBarPublicSensor
    {
        private readonly ConcurrentDictionary<string, long> _queuesInfo = new ConcurrentDictionary<string, long>();


        protected BaseQueueInfoIntSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(string queueName, int value)
        {
            if (!_queuesInfo.ContainsKey(queueName))
                _queuesInfo.TryAdd(queueName, value);
            else
                _queuesInfo[queueName] += value;
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

            foreach (var pair in _queuesInfo)
                if (pair.Value > 0)
                    sb.AppendLine($"{pair.Key}: {pair.Value}");

            _queuesInfo.Clear();

            return sb.ToString();
        }
    }
}
