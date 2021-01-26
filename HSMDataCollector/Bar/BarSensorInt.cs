using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using HSMSensorDataObjects;

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

        protected override void SendDataTimer(object state)
        {
            IntBarSensorValue dataObject = GetDataObject();
            ThreadPool.QueueUserWorkItem(_ => SendData(dataObject));
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

        private IntBarSensorValue GetDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            lock (_syncRoot)
            {
                result.Max = Max;
                Max = 0;
                result.Min = Min;
                Min = 0;
                result.Mean = Mean;
                Mean = 0;
                result.Count = ValuesCount;
                ValuesCount = 0;
                _sum = 0;
            }

            result.Key = ProductKey;
            result.Path = Path;
            result.Time = DateTime.Now;
            return result;
        }
        protected override byte[] GetBytesData(object data)
        {
            IntBarSensorValue typedData = (IntBarSensorValue) data;
            string convertedString = JsonSerializer.Serialize(typedData);
            return Encoding.UTF8.GetBytes(convertedString);
        }
    }
}
