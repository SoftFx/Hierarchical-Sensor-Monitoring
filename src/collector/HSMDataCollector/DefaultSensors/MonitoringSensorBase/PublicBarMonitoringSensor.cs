using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.DefaultSensors
{
    internal class PublicBarMonitoringSensor<BarType, T> : BarMonitoringSensorBase<BarType, T>, IBarSensor<T>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        protected override string SensorName => throw new NotImplementedException();


        public PublicBarMonitoringSensor(BarSensorOptions options) : base(options)
        {
        }


        public void AddValue(T value) => _internalBar.AddValue(value);

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
        public DoubleBarPublicSensor(BarSensorOptions options) : base (options) { }
    }
}
