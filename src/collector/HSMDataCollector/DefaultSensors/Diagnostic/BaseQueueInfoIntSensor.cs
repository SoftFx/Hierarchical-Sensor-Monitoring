using HSMDataCollector.Options;
using System.Collections.Concurrent;
using System.Text;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal abstract class IntQueueInfoSensor : BaseQueueInfoSensor<IntMonitoringBar, int>
    {
        protected IntQueueInfoSensor(BarSensorOptions options) : base(options) { }


        protected override int Apply(int oldValue, int newValue) => oldValue + newValue;

        protected override bool ApplyToComment(int value) => value > 0;
    }


    internal abstract class DoubleQueueInfoSensor : BaseQueueInfoSensor<DoubleMonitoringBar, double>
    {
        protected DoubleQueueInfoSensor(BarSensorOptions options) : base(options) { }


        protected override double Apply(double oldValue, double newValue) => oldValue + newValue;

        protected override bool ApplyToComment(double value) => value > 0.0;
    }


    internal abstract class BaseQueueInfoSensor<TBarType, TData> : PublicBarMonitoringSensor<TBarType, TData>
        where TBarType : MonitoringBarBase<TData>, new()
        where TData : struct
    {
        private readonly ConcurrentDictionary<string, TData> _queuesInfo = new ConcurrentDictionary<string, TData>();


        protected BaseQueueInfoSensor(BarSensorOptions options) : base(options) { }


        protected abstract TData Apply(TData oldValue, TData newValue);

        protected abstract bool ApplyToComment(TData value);


        internal void AddValue(string queueName, TData value)
        {
            _queuesInfo.AddOrUpdate(queueName, value, (k, v) => Apply(v, value));

            AddValue(value);
        }


        protected override string GetComment() => GetQueueStats();

        protected override void BuildNewBar()
        {
            base.BuildNewBar();

            _queuesInfo.Clear();
        }


        private string GetQueueStats()
        {
            var sb = new StringBuilder(1 << 10);

            foreach (var pair in _queuesInfo)
                if (ApplyToComment(pair.Value))
                    sb.AppendLine($"{pair.Key}: {pair.Value}");

            return sb.ToString();
        }
    }
}