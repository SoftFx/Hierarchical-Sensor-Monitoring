using System;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Base;
using HSMDataCollector.Core;

namespace HSMDataCollector.Bar
{
    public abstract class BarSensorBase : SensorBase, IDisposable
    {
        private Timer _barTimer;
        private Timer _smallTimer;
        protected object _syncObject;
        protected DateTime barStart;
        private TimeSpan _barTimerSpan;
        private TimeSpan _smallTimerSpan;
        /// <summary>
        /// </summary>
        /// <param name="path"></param>
        /// <param name="productKey"></param>
        /// <param name="collectPeriod">One bar contains data for the given period. 5000 is 5 seconds.</param>
        /// <param name="smallPeriod">The sensor sends intermediate bar data every smallPeriod time.</param>
        protected BarSensorBase(string path, string productKey,
            IValuesQueue queue, TimeSpan barTimerPeriod, TimeSpan smallTimerPeriod)
            : base(path, productKey, queue)
        {
            _syncObject = new object();
            _barTimerSpan = barTimerPeriod;
            _smallTimerSpan = smallTimerPeriod;
            StartTimer(_barTimerSpan, _smallTimerSpan);
        }
        protected abstract void SendDataTimer(object state);
        protected abstract void SmallTimerTick(object state);

        public void Restart(int barPeriod, int smallPeriod)
        {
            Restart(TimeSpan.FromMilliseconds(barPeriod), TimeSpan.FromMilliseconds(smallPeriod));
        }
        public void Restart(TimeSpan barTimerSpan, TimeSpan smallTimerSpan)
        {
            if (_barTimerSpan != barTimerSpan || _smallTimerSpan != smallTimerSpan)
            {
                _barTimerSpan = barTimerSpan;
                _smallTimerSpan = smallTimerSpan;
                StartTimer(_barTimerSpan, _smallTimerSpan);
            }
        }
        private void StartTimer(TimeSpan barTimeSpan, TimeSpan smallTimerSpan)
        {
            _barTimer?.Dispose();
            _smallTimer?.Dispose();
            _barTimer = new Timer(SendDataTimer, null, barTimeSpan, barTimeSpan);
            _smallTimer = new Timer(SmallTimerTick, null, smallTimerSpan, smallTimerSpan);
            barStart = DateTime.Now;
        }
        protected void Stop()
        {
            _barTimer.Dispose();
            _barTimer = null;
            _smallTimer.Dispose();
            _smallTimer = null;
        }

        protected double GetPercentile(List<double> values, double percent)
        {
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
            return Math.Round(leftNumber + part * (rightNumber - leftNumber));
        }

        protected int GetPercentile(List<int> values, double percent)
        {
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
