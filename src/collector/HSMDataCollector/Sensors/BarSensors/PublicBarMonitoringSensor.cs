using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System.Collections.Generic;

namespace HSMDataCollector.DefaultSensors
{
    internal class PublicBarMonitoringSensor<BarType, T> : BarMonitoringSensorBase<BarType, T>, IBarSensor<T>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        protected override string SensorName { get; }


        public PublicBarMonitoringSensor(BarSensorOptions options) : base(options)
        {
            SensorName = options.SensorName;
        }


        public void AddValue(T value) => _internalBar.AddValue(value);

        public void AddPartial(T min, T max, T mean, T _, T last, int count) =>
            _internalBar.AddPartial(min, max, mean, last, count);

        public void AddValues(IEnumerable<T> values)
        {
            foreach (var value in values)
                AddValue(value);
        }
    }


    internal class IntBarPublicSensor : PublicBarMonitoringSensor<IntMonitoringBar, int>
    {
        public IntBarPublicSensor(BarSensorOptions options) : base(options) { }
    }


    internal class DoubleBarPublicSensor : PublicBarMonitoringSensor<DoubleMonitoringBar, double>
    {
        public DoubleBarPublicSensor(BarSensorOptions options) : base(options) { }
    }
}
