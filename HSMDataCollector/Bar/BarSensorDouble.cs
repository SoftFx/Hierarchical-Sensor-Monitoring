using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.Bar
{
    public class BarSensorDouble : BarSensorBase<double>
    {
        private double _sum = 0.0;
        public BarSensorDouble(string name, string path, string productKey, string serverAddress, int collectPeriod = 5000)
            : base(name, path, productKey, serverAddress, collectPeriod)
        {
            Max = double.MinValue;
            Min = double.MaxValue;
            Mean = 0.0;
        }

        protected override void SendData(object state)
        {
            throw new NotImplementedException();
        }

        public override void AddValue(object value)
        {
            double doubleValue = (double) value;
            lock (_syncRoot)
            {
                if (doubleValue > Max)
                {
                    Max = doubleValue;
                }

                if (doubleValue < Min)
                {
                    Min = doubleValue;
                }

                ++ValuesCount;
                _sum += doubleValue;
                Mean = _sum / ValuesCount;
            }
        }
    }
}
