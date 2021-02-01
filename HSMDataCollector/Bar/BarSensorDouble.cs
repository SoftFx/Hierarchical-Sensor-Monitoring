using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using HSMSensorDataObjects;

namespace HSMDataCollector.Bar
{
    public class BarSensorDouble : BarSensorBase<double>
    {
        private double _sum = 0.0;
        public BarSensorDouble(string path, string productKey, string serverAddress, int collectPeriod = 5000)
            : base(path, productKey, serverAddress, collectPeriod)
        {
            Max = double.MinValue;
            Min = double.MaxValue;
            Mean = 0.0;
        }

        protected override void SendDataTimer(object state)
        {
            DoubleBarSensorValue dataObject = GetDataObject();
            ThreadPool.QueueUserWorkItem(_ => SendData(dataObject));
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

        private DoubleBarSensorValue GetDataObject()
        {
            DoubleBarSensorValue result = new DoubleBarSensorValue();
            lock (_syncRoot)
            {
                result.Max = Max;
                Max = 0.0;
                result.Min = Min;
                Min = 0.0;
                result.Mean = Mean;
                Mean = 0.0;
                result.Count = ValuesCount;
                ValuesCount = 0;
                _sum = 0.0;
            }

            result.Key = ProductKey;
            result.Path = Path;
            result.Time = DateTime.Now;
            return result;
        }

        protected override byte[] GetBytesData(object data)
        {
            DoubleBarSensorValue typedData = (DoubleBarSensorValue) data;
            string convertedString = JsonSerializer.Serialize(typedData);
            return Encoding.UTF8.GetBytes(convertedString);
        }
    }
}
