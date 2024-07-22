using System;
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
            try
            {
                CheckCurrentBar();

                _internalBar.AddValue(value);
            }
            catch (Exception ex) { HandleException(ex); }
        }

        public void AddPartial(T min, T max, T mean, T first, T last, int count)
        {
            try
            {
                CheckCurrentBar();

                _internalBar.AddPartial(min, max, mean, first, last, count);
            }
            catch (Exception ex) { HandleException(ex);}
        }


        public void AddValues(IEnumerable<T> values)
        {
            try
            {
                foreach (var value in values)
                    AddValue(value);
            }
            catch (Exception ex) { HandleException(ex); }
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