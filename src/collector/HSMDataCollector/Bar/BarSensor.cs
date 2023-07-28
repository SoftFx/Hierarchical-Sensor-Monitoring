using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

namespace HSMDataCollector.Bar
{
    internal abstract class PublicBarSensorBase<BarType, BarValueType> : BarMonitoringSensorBase<BarType, BarValue<BarValueType>, BarValueType>
        where BarType : MonitoringBarBase<BarValue<BarValueType>, BarValueType>, new()
        where BarValueType : struct, IComparable<BarValueType>
    {
        private readonly object _lock = new object();
        private readonly BarBuilder<BarValueType> _barBuilder;

        public PublicBarSensorBase(string name, BarSensorOptions options, int precision) : base(options)
        {
            SensorName = name;
            _barBuilder = InitBarBuilder(precision);
        }

        protected override string SensorName { get; }

        public void AddValue(BarValueType value)
        {
            lock (_lock)
            {
                _barBuilder.AddValue(new BarValue<BarValueType>(value));
            }
        }

        protected override BarValue<BarValueType> GetBarData()
        {
            lock (_lock)
            {
                return _barBuilder.GetCurrentAndReset();
            }
        }

        protected abstract BarBuilder<BarValueType> InitBarBuilder(int precision);

        public SensorValueBase GetLastValue()
        {
            return GetValue();
        }
    }

    internal class PublicIntBarSensor : PublicBarSensorBase<IntMonitoringBarSensor, int>
    {
        public PublicIntBarSensor(string name, BarSensorOptions options) : base(name, options, 0) { }

        protected override BarBuilder<int> InitBarBuilder(int precision) => new IntBarBuilder(); 
    }

    internal class PublicDoubleBarSensor : PublicBarSensorBase<DoubleMonitoringBarSensor, double>
    {
        public PublicDoubleBarSensor(string name, int precision, BarSensorOptions options) : base(name, options, precision) { }
        protected override BarBuilder<double> InitBarBuilder(int precision) => new DoubleBarBuider(precision);       
    }

    internal sealed class IntMonitoringBarSensor : MonitoringBarBase<BarValue<int>, int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;

        protected override IBarBuilder<BarValue<int>, int> InitBarBuilder(int precision) => new IntBarBuilder();
    }

    internal sealed class DoubleMonitoringBarSensor : MonitoringBarBase<BarValue<double>, double>
    {
        public override SensorType Type => SensorType.DoubleBarSensor;

        protected override IBarBuilder<BarValue<double>, double> InitBarBuilder(int precision) => new DoubleBarBuider(precision);
    }
}
