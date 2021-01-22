using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.Bar
{
    public class BarSensorInt : BarSensorBase<int>
    {
        private long _sum = 0;
        public BarSensorInt(string name, string path, string productKey, string serverAddress, int collectPeriod = 5000)
            : base(name, path, productKey, serverAddress, collectPeriod)
        {
            Min = Int32.MaxValue;
            Max = Int32.MinValue;
            Mean = 0;
        }

        protected override void SendData(object state)
        {

        }

        public override void AddValue(object value)
        {
            int intValue = (int) value;
            lock (_syncRoot)
            {
                if (intValue < Min)
                {
                    Min = intValue;
                }

                if (intValue > Max)
                {
                    Max = intValue;
                }

                ++ValuesCount;
                _sum += intValue;
                Mean = (int) (_sum / ValuesCount);
            }
        }
    }
}
