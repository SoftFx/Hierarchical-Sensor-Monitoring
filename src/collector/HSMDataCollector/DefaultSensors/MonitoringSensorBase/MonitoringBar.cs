﻿using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<T> : BarSensorValueBase<T>
    {
        private readonly object _lock = new object();
        protected double _totalSum = 0.0;

        internal int Precision { get; private set; }


        internal void AddValue(T value)
        {
            lock (_lock)
            {
                if (DateTime.UtcNow > CloseTime)
                    return;

                if (Count == 0)
                {
                    Mean = value;
                    Min = value;
                    Max = value;
                }
                else
                    ApplyNewValue(value);

                LastValue = value;
                Count++;
            }
        }

        internal void Init(TimeSpan timerPeriod, int precision)
        {
            OpenTime = timerPeriod.GetOpenTime();
            CloseTime = OpenTime + timerPeriod;
            Precision = precision;
        }

        internal MonitoringBarBase<T> Complete()
        {
            lock (_lock)
            {
                if (Count > 0)
                {
                    LastValue = Round(LastValue);

                    Min = Round(Min);
                    Max = Round(Max);
                    Mean = Round(CountMean());

                    Percentiles[0.25] = Round(CountAvr(Mean, Min));
                    Percentiles[0.5] = Mean;
                    Percentiles[0.75] = Round(CountAvr(Mean, Max));
                }

                return this;
            }
        }


        protected abstract void ApplyNewValue(T value);


        protected abstract T CountAvr(T first, T second);

        protected abstract T Round(T value);

        protected abstract T CountMean();
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

        protected override int CountAvr(int first, int second) => (first + second) / 2;

        protected override int CountMean() => (int)Math.Round(_totalSum / Count);

        protected override int Round(int value) => value;
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

        protected override double CountAvr(double first, double second) => (first + second) / 2;

        protected override double CountMean() => _totalSum / Count;

        protected override double Round(double value) => Math.Round(value, Precision, MidpointRounding.AwayFromZero);
    }
}
