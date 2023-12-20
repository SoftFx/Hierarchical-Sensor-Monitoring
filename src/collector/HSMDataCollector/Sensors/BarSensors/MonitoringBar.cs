using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<T> : BarSensorValueBase<T> where T : struct
    {
        private readonly object _lock = new object();

        protected double _totalSum;

        internal int Precision { get; private set; }


        internal void Init(TimeSpan timerPeriod, int precision)
        {
            OpenTime = timerPeriod.GetOpenTime();
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
                }

                return this;
            }
        }


        protected abstract void ApplyNewValue(T value);

        protected abstract void ApplyPartial(T min, T max);


        protected abstract T CountAvr(T first, T second);

        protected abstract T Round(T value);

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


        protected override void CountSum(double mean, int count) => _totalSum += mean * count;
    }
}