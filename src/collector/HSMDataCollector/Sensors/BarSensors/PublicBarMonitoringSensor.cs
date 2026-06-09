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

        /// <summary>
        /// Checks that <paramref name="mean"/>, <paramref name="first"/>, and <paramref name="last"/>
        /// all lie inside <c>[min, max]</c> using <see cref="Comparer{T}.Default"/>. Overridden in
        /// floating-point closures (see <see cref="DoubleBarPublicSensor"/>) to tolerate FP
        /// rounding — a caller-computed mean for a double bar can legitimately land an epsilon
        /// outside the observed [min, max] range and the strict comparison would silently drop it.
        /// </summary>
        protected virtual bool IsValidPartial(T min, T max, T mean, T first, T last, int count)
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
        // Absolute floor + range-relative term. The mean computed by a producer for a double bar
        // can drift by a few ULPs (units in the last place) past the observed [min, max] when the
        // sample count is large, which is harmless and should not cause the bar to be silently
        // dropped. Tolerance scales with the bar's value range so it remains meaningful across
        // different sensor magnitudes (e.g. memory bytes vs CPU percent).
        private const double AbsoluteEpsilon = 1e-12;
        private const double RelativeEpsilon = 1e-9;


        internal DoubleBarPublicSensor(BarSensorOptions options) : base(options) { }


        protected override bool IsValidPartial(double min, double max, double mean, double first, double last, int count)
        {
            if (count < 1)
                return false;

            var tolerance = Math.Max(AbsoluteEpsilon, Math.Abs(max - min) * RelativeEpsilon);

            return min <= max + tolerance &&
                   mean >= min - tolerance && mean <= max + tolerance &&
                   first >= min - tolerance && first <= max + tolerance &&
                   last >= min - tolerance && last <= max + tolerance;
        }
    }
}
