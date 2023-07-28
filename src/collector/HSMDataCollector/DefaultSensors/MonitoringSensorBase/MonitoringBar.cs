using HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<T> : BarSensorValueBase<T>
        where T : IComparable<T>
    {
        private readonly object _lock = new object();
        private BarBuilder<T> _barBuilder;

        internal void Init(TimeSpan timerPeriod, int precision)
        {
            OpenTime = timerPeriod.GetOpenTime();
            CloseTime = OpenTime + timerPeriod;
            _barBuilder = InitBarBuilder(precision);
        }

        internal void AddValue(T value)
        {
            lock (_lock)
            {
                _barBuilder.AddValue(value);
            }
        }

        internal void AddValue(BarValue<T> barValue)
        {
            lock (_lock)
            {
                _barBuilder.AddValue(barValue);
            }
        }

        internal MonitoringBarBase<T> Complete()
        {
            lock (_lock)
            {
                _barBuilder.FillBarFields(this);
                return this;
            }
        }

        protected abstract BarBuilder<T> InitBarBuilder(int precision);
    }

    public sealed class IntMonitoringBar : MonitoringBarBase<int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;

        protected override BarBuilder<int> InitBarBuilder(int precision) => new IntBarBuilder();
    }

    public sealed class DoubleMonitoringBar : MonitoringBarBase<double>
    {
        public override SensorType Type => SensorType.DoubleBarSensor;

        protected override BarBuilder<double> InitBarBuilder(int precision) => new DoubleBarBuider(precision);
    }
}
