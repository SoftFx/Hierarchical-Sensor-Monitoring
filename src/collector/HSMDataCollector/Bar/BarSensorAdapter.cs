using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace HSMDataCollector.Bar
{
    internal abstract class BarSensorAdapter<BarSensorType, BarType, BarValueType> : Base.SensorBase, IBarSensor<BarValueType>
        where BarSensorType : PublicBarSensorBase<BarType, BarValueType>
        where BarValueType : struct, IComparable<BarValueType>
        where BarType : MonitoringBarBase<BarValueType>, new()
    {
        private readonly PublicBarSensorBase<BarType, BarValueType> _sensor;

        public BarSensorAdapter(string path, IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod, string description, int precision)
            : base(path, queue, description)
        {
            var split = path.Split('/');
            var name = split.LastOrDefault();
            var nodePath = path.Substring(0, path.Length - name.Length - 1);
            _sensor = InitBarSensor(name, new Options.BarSensorOptions { BarPeriod = TimeSpan.FromMilliseconds(barTimerPeriod), PostDataPeriod = TimeSpan.FromMilliseconds(smallTimerPeriod), NodePath = nodePath }, precision);
            _sensor.ReceiveSensorValue += queue.Push;
        }

        public override void Start()
        {
            _sensor.Init().Wait();
        }

        public override bool HasLastValue => true;

        public void AddValue(BarValueType value)
        {
            _sensor.AddValue(value);
        }

        public override void Dispose()
        {
            _sensor.Dispose();
        }

        public override SensorValueBase GetLastValue()
        {
            return _sensor.GetLastValue();
        }

        protected abstract PublicBarSensorBase<BarType, BarValueType> InitBarSensor(string name, Options.BarSensorOptions options, int precision);
    }

    internal class DoubleBarSensorAdapter : BarSensorAdapter<PublicDoubleBarSensor, DoubleMonitoringBar, double>
    {
        private readonly PublicDoubleBarSensor _sensor;

        public DoubleBarSensorAdapter(string path, IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod, string description, int precision) : base(path, queue, barTimerPeriod, smallTimerPeriod, description, precision)
        {
        }

        protected override PublicBarSensorBase<DoubleMonitoringBar, double> InitBarSensor(string name, Options.BarSensorOptions options, int precision) => new PublicDoubleBarSensor(name, precision, options);
    }

    internal class IntBarSensorAdapter : BarSensorAdapter<PublicIntBarSensor, IntMonitoringBar, int>
    {
        public IntBarSensorAdapter(string path, IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod, string description) : base(path, queue, barTimerPeriod, smallTimerPeriod, description, 0)
        {
        }

        protected override PublicBarSensorBase<IntMonitoringBar, int> InitBarSensor(string name, BarSensorOptions options, int precision)  => new PublicIntBarSensor(name, options);
    }
}
