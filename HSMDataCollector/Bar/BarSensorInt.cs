using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.Bar
{
    public class BarSensorInt : BarSensorBase<int>
    {
        public BarSensorInt(string path, string productKey, string serverAddress, int collectPeriod = 30000)
            : base(path, productKey, $"{serverAddress}/intBar", collectPeriod)
        {
            Min = int.MaxValue;
            Max = int.MinValue;
        }

        protected override void SendDataTimer(object state)
        {
            IntBarSensorValue dataObject = GetDataObject();
            //ThreadPool.QueueUserWorkItem(_ => SendData(dataObject));
            //Task.Run(() => SendData(dataObject));
            SendData(dataObject);
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
                ValuesList.Add(intValue);
            }
        }

        private IntBarSensorValue GetDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            lock (_syncRoot)
            {
                result.Max = Max;
                Max = int.MinValue;
                result.Min = Min;
                Min = int.MaxValue;
                result.Mean = CountMean();
                result.Count = ValuesCount;
                ValuesCount = 0;
                ValuesList.Clear();
            }

            result.Key = ProductKey;
            result.Path = Path;
            result.Time = DateTime.Now;
            return result;
        }

        protected override byte[] GetBytesData(object data)
        {
            try
            {
                IntBarSensorValue typedData = (IntBarSensorValue)data;
                string convertedString = JsonSerializer.Serialize(typedData);
                return Encoding.UTF8.GetBytes(convertedString);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new byte[1];
            }
            
        }

        private int CountMean()
        {
            long sum = ValuesList.Sum();
            return (int) (sum / ValuesCount);
        }
    }
}
