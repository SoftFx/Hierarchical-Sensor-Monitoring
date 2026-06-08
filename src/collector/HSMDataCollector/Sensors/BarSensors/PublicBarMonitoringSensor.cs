using System;
using System.Collections.Generic;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;


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
                if (!SensorValueExtensions.IsValidValue(value, SensorStatus.Ok))
                {
                    _dataProcessor?.LogDroppedValue(SensorPath, "bar sample failed validation (NaN/Infinity/null)");
                    return;
                }

                CheckCurrentBar();

                _internalBar.AddValue(value);
            }
            catch (Exception ex) { HandleException(ex); }
        }

        public void AddPartial(T min, T max, T mean, T first, T last, int count)
        {
            try
            {
                if (!SensorValueExtensions.IsValidValue(min, SensorStatus.Ok) ||
                    !SensorValueExtensions.IsValidValue(max, SensorStatus.Ok) ||
                    !SensorValueExtensions.IsValidValue(mean, SensorStatus.Ok) ||
                    !SensorValueExtensions.IsValidValue(first, SensorStatus.Ok) ||
                    !SensorValueExtensions.IsValidValue(last, SensorStatus.Ok))
                {
                    _dataProcessor?.LogDroppedValue(SensorPath, "partial bar contains NaN/Infinity/null in one of min/max/mean/first/last");
                    return;
                }

                if (!IsValidPartial(min, max, mean, first, last, count))
                {
                    _dataProcessor?.LogDroppedValue(SensorPath, $"partial bar stats inconsistent: count={count}, mean/first/last outside [min, max]");
                    return;
                }

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

        private static bool IsValidPartial(T min, T max, T mean, T first, T last, int count)
        {
            if (count < 1)
                return false;

            var comparer = Comparer<T>.Default;

            return comparer.Compare(min, max) <= 0 &&
                   comparer.Compare(mean, min) >= 0 &&
                   comparer.Compare(mean, max) <= 0 &&
                   comparer.Compare(first, min) >= 0 &&
                   comparer.Compare(first, max) <= 0 &&
                   comparer.Compare(last, min) >= 0 &&
                   comparer.Compare(last, max) <= 0;
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
