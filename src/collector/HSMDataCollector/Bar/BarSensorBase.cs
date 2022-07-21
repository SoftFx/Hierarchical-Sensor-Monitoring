﻿using HSMDataCollector.Base;
using HSMDataCollector.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HSMDataCollector.Bar
{
    public abstract class BarSensorBase : SensorBase, IDisposable
    {
        private Timer _barTimer;
        private Timer _smallTimer;
        protected object _syncObject;
        protected DateTime barStart;
        protected int _barTimerPeriod;
        private int _smallTimerPeriod;
        protected int _precision;
        /// <summary>
        /// </summary>
        /// <param name="path"></param>
        /// <param name="productKey"></param>
        /// <param name="collectPeriod">One bar contains data for the given period. 5000 is 5 seconds.</param>
        /// <param name="smallPeriod">The sensor sends intermediate bar data every smallPeriod time.</param>
        protected BarSensorBase(string path, string productKey,
            IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod, string description, int precision)
            : base(path, productKey, queue, description)
        {
            _syncObject = new object();
            _barTimerPeriod = barTimerPeriod;
            _smallTimerPeriod = smallTimerPeriod;
            FillPrecision(precision);
            StartTimer(_barTimerPeriod, _smallTimerPeriod);
        }
        private void FillPrecision(int precision)
        {
            if (precision < 1 || precision > 10)
            {
                _precision = 2;
                return;
            }

            _precision = precision;
        }
        protected abstract void SendDataTimer(object state);
        protected abstract void SmallTimerTick(object state);

        public void Restart(int barPeriod, int smallPeriod)
        {
            RestartInternal(barPeriod, smallPeriod);
        }
        private void RestartInternal(int barTimerPeriod, int smallTimerPeriod)
        {
            if (_barTimerPeriod != barTimerPeriod || _smallTimerPeriod != smallTimerPeriod)
            {
                _barTimerPeriod = barTimerPeriod;
                _smallTimerPeriod = smallTimerPeriod;
                StartTimer(_barTimerPeriod, _smallTimerPeriod);
            }
        }
        private void StartTimer(int barTimePeriod, int smallTimerPeriod)
        {
            Stop();

            _smallTimer = new Timer(SmallTimerTick, null, TimeSpan.FromMilliseconds(smallTimerPeriod), TimeSpan.FromMilliseconds(smallTimerPeriod));

            _barTimer = new Timer(SendDataTimer, null, GetSpanUntilFirstTick(barTimePeriod), TimeSpan.FromMilliseconds(barTimePeriod));
            barStart = DateTime.Now;
        }

        private TimeSpan GetSpanUntilFirstTick(int period)
        {
            DateTime firstTime = DateTime.MinValue;
            if (period == 3600000)
            {
                firstTime = DateTime.Now.AddHours(1).Subtract(TimeSpan.FromMinutes(DateTime.Now.Minute)).Subtract(TimeSpan.FromSeconds(DateTime.Now.Second));
            }

            if (period == 1800000)
            {
                firstTime = DateTime.Now.AddMinutes(30 - DateTime.Now.Minute % 30)
                    .Subtract(TimeSpan.FromSeconds(DateTime.Now.Second));
            }

            if (period == 600000)
            {
                firstTime = DateTime.Now.AddMinutes(10 - DateTime.Now.Minute % 10).Subtract(TimeSpan.FromSeconds(DateTime.Now.Second));
            }

            if (period == 300000)
            {
                firstTime = DateTime.Now.AddMinutes(5 - DateTime.Now.Minute % 5).Subtract(TimeSpan.FromSeconds(DateTime.Now.Second));
            }

            if (period == 60000)
            {
                firstTime = DateTime.Now.AddMinutes(1).Subtract(TimeSpan.FromSeconds(DateTime.Now.Second));
            }

            if (firstTime != DateTime.MinValue)
            {
                return firstTime - DateTime.Now;
            }
            return TimeSpan.FromMilliseconds(period);
        }
        protected void Stop()
        {
            _barTimer?.Dispose();
            _barTimer = null;
            _smallTimer?.Dispose();
            _smallTimer = null;
        }

        protected double GetPercentile(List<double> values, double percent)
        {
            if (!values.Any())
                return 0.0;

            if (values.Count == 1)
            {
                return values[0];
            }

            double position = (values.Count + 1) * percent / 100;
            double leftNumber = 0.0d;
            double rightNumber = 0.0d;

            double n = percent / 100.0d * (values.Count - 1) + 1.0d;
            if (position > 1)
            {
                leftNumber = values[(int) Math.Floor(n) - 1];
                rightNumber = values[(int) Math.Floor(n)];
            }
            else
            {
                leftNumber = values[0];
                rightNumber = values[1];
            }

            if (leftNumber.Equals(rightNumber))
                return leftNumber;

            double part = n - Math.Floor(n);
            return Math.Round(leftNumber + part * (rightNumber - leftNumber), _precision, MidpointRounding.AwayFromZero);
        }

        protected int GetPercentile(List<int> values, double percent)
        {
            if (!values.Any())
                return 0;

            if (values.Count == 1)
            {
                return values[0];
            }

            var count = values.Count;
            int index = (int)Math.Floor(count * percent);
            return values[index];
        }

        public override bool HasLastValue => true;

        public override void Dispose()
        {
            _barTimer?.Dispose();
            _smallTimer?.Dispose();
        }
    }
}
