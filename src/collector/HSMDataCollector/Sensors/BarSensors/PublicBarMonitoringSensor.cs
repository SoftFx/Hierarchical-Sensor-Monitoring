using System;
using System.Collections.Generic;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;


namespace HSMDataCollector.DefaultSensors
{
    public class PublicBarMonitoringSensor<BarType, T> : BarMonitoringSensorBase<BarType, T>, IBarSensor<T>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        internal PublicBarMonitoringSensor(BarSensorOptions options) : base(options) { }


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


    public class IntBarPublicSensor : PublicBarMonitoringSensor<IntMonitoringBar, int>
    {
        internal IntBarPublicSensor(BarSensorOptions options) : base(options) { }
    }


    public class DoubleBarPublicSensor : PublicBarMonitoringSensor<DoubleMonitoringBar, double>
    {
        internal DoubleBarPublicSensor(BarSensorOptions options) : base(options) { }
    }
}