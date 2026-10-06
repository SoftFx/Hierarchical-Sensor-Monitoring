using System;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<T> : BarSensorValueBase<T> where T : struct
    {
        private readonly object _lock = new object();

        protected double _totalSum;

        // Welford moments for StdDev (#1509): a running mean and the sum of squared deviations
        // from it (M2), O(1) per sample and no sample storage. Kept apart from _totalSum so the
        // wire Mean stays exactly what it was. The native collector runs the same operations in
        // the same order (MonitoringBar::AddValue in hsm_collector.cpp) so both post identical
        // bytes for identical samples. A pre-aggregated partial carries no spread, so it makes
        // the bar's StdDev unknown (null) instead of guessing.
        private double _welfordMean;
        private double _welfordM2;
        private bool _stdDevUnknown;

        internal int Precision { get; private set; }


        internal void Init(TimeSpan timerPeriod, int precision)
        {
            OpenTime  = BarTimeHelper.GetOpenTime(timerPeriod);
            CloseTime = OpenTime + timerPeriod;
            Precision = precision;
        }

        internal void AddValue(T value)
        {
            lock (_lock)
            {
                if (Count == 0)
                {
                    FirstValue = value;
                    Mean = value;
                    Min = value;
                    Max = value;

                    CountSum(value, 1);
                }
                else
                    ApplyNewValue(value);

                AddMoments(ToDouble(value));

                LastValue = value;
                Count++;
            }
        }

        internal void AddPartial(T min, T max, T mean, T first, T last, int count)
        {
            lock (_lock)
            {
                if (count < 1)
                    return;

                if (Count == 0)
                {
                    FirstValue = first;
                    Mean = mean;
                    Min = min;
                    Max = max;
                }
                else
                    ApplyPartial(min, max);

                CountSum(mean, count);
                LastValue = last;
                Count += count;

                _stdDevUnknown = true;
            }
        }

        internal MonitoringBarBase<T> Complete()
        {
            lock (_lock)
            {
                if (Count > 0)
                {
                    FirstValue = FirstValue.HasValue ? Round(FirstValue.Value) : FirstValue;
                    LastValue = Round(LastValue);

                    Min = Round(Min);
                    Max = Round(Max);
                    Mean = Round(CountMean());
                    StdDev = _stdDevUnknown ? (double?)null : RoundStdDev(CountStdDev());
                }

                return this;
            }
        }


        // Population standard deviation of the samples added so far; 0 for a single sample.
        private double CountStdDev() => Count <= 1 ? 0.0 : Math.Sqrt(_welfordM2 / Count);

        // Must be called BEFORE Count is incremented for this sample. Operation order is part of
        // the cross-language wire contract — keep it identical to the native transcription.
        private void AddMoments(double value)
        {
            double n = Count + 1;
            double delta = value - _welfordMean;

            _welfordMean += delta / n;
            _welfordM2 += delta * (value - _welfordMean);
        }


        protected abstract void ApplyNewValue(T value);

        protected abstract void ApplyPartial(T min, T max);


        protected abstract T CountAvr(T first, T second);

        protected abstract T Round(T value);

        protected abstract double ToDouble(T value);

        protected abstract double RoundStdDev(double value);

        protected abstract T CountMean();

        protected abstract void CountSum(T mean, int count);


        internal MonitoringBarBase<T> Copy() => (MonitoringBarBase<T>)MemberwiseClone();
    }


    public sealed class IntMonitoringBar : MonitoringBarBase<int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;


        protected override void ApplyNewValue(int value)
        {
            _totalSum += value;

            Min = Math.Min(value, Min);
            Max = Math.Max(value, Max);
        }

        protected override void ApplyPartial(int min, int max)
        {
            Min = Math.Min(min, Min);
            Max = Math.Max(max, Max);
        }


        protected override int CountAvr(int first, int second) => (first + second) / 2;

        protected override int CountMean() => (int)Math.Round(_totalSum / Count);

        protected override int Round(int value) => value;

        protected override double ToDouble(int value) => value;

        // An int bar has no precision of its own (its option is ignored), so its StdDev is rounded
        // to a fixed 2 digits, the same constant the native collector uses.
        protected override double RoundStdDev(double value) => Math.Round(value, IntBarStdDevDigits, MidpointRounding.AwayFromZero);

        internal const int IntBarStdDevDigits = 2;


        protected override void CountSum(int mean, int count) => _totalSum += (double)mean * count;
    }


    public sealed class DoubleMonitoringBar : MonitoringBarBase<double>
    {
        public override SensorType Type => SensorType.DoubleBarSensor;


        protected override void ApplyNewValue(double value)
        {
            _totalSum += value;

            Min = Math.Min(value, Min);
            Max = Math.Max(value, Max);
        }

        protected override void ApplyPartial(double min, double max)
        {
            Min = Math.Min(min, Min);
            Max = Math.Max(max, Max);
        }


        protected override double CountAvr(double first, double second) => (first + second) / 2;

        protected override double CountMean() => _totalSum / Count;

        protected override double Round(double value) => Math.Round(value, Precision, MidpointRounding.AwayFromZero);

        protected override double ToDouble(double value) => value;

        protected override double RoundStdDev(double value) => Round(value);


        protected override void CountSum(double mean, int count) => _totalSum += mean * count;
    }
}