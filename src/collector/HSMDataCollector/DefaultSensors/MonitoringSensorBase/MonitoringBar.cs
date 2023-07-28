using HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<AddValueType, BarValueType> : BarSensorValueBase<BarValueType>
    {
        private readonly object _lock = new object();
        private IBarBuilder<AddValueType, BarValueType> _barBuilder;

        internal void Init(TimeSpan timerPeriod, int precision)
        {
            OpenTime = timerPeriod.GetOpenTime();
            CloseTime = OpenTime + timerPeriod;
            _barBuilder = InitBarBuilder(precision);
        }

        internal void AddValue(AddValueType value)
        {
            lock (_lock)
            {
                _barBuilder.AddValue(value);
            }
        }

        internal MonitoringBarBase<AddValueType, BarValueType> Complete()
        {
            lock (_lock)
            {
                _barBuilder.FillBarFields(this);
                return this;
            }
        }

        protected abstract IBarBuilder<AddValueType, BarValueType> InitBarBuilder(int precision);
    }

    public sealed class IntMonitoringBar : MonitoringBarBase<int, int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;

        protected override IBarBuilder<int, int> InitBarBuilder(int precision) => new IntListBarBuilder();
    }


    public sealed class DoubleMonitoringBar : MonitoringBarBase<double, double>
    {
        public override SensorType Type => SensorType.DoubleBarSensor;

        protected override IBarBuilder<double, double> InitBarBuilder(int precision) => new DoubleListBarBuilder(precision);
    }
}
