using System.Collections.Generic;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;


namespace HSMDataCollector.DefaultSensors
{
    internal class PublicBarMonitoringSensor<BarType, T> : BarMonitoringSensorBase<BarType, T>, IBarSensor<T>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        public PublicBarMonitoringSensor(BarSensorOptions options) : base(options) { }


        public void AddValue(T value)
        {
            CheckCurrentBar();

            _internalBar.AddValue(value);
        }

        public void AddPartial(T min, T max, T mean, T first, T last, int count)
        {
            CheckCurrentBar();

            _internalBar.AddPartial(min, max, mean, first, last, count);
        }


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