using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.Bar
{
    public class BarSensorInt : BarSensorBase<int>, IIntBarSensor
    {
        public BarSensorInt(string path, string productKey, string serverAddress, HttpClient client, int collectPeriod = 30000)
            : base(path, productKey, $"{serverAddress}/intBar", client, collectPeriod)
        {
            Min = int.MaxValue;
            Max = int.MinValue;
        }

        protected override void SendDataTimer(object state)
        {
            IntBarSensorValue dataObject = GetDataObject();
            //ThreadPool.QueueUserWorkItem(_ => SendData(dataObject));
            //Task.Run(() => SendData(dataObject));
            string serializedValue = GetStringData(dataObject);
            SendData(serializedValue);
        }

        public void AddValue(int value)
        {
            lock (_syncObject)
            {
                if (value < Min)
                {
                    Min = value;
                }

                if (value > Max)
                {
                    Max = value;
                }

                ++ValuesCount;
                ValuesList.Add(value);
            }
        }

        private IntBarSensorValue GetDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            lock (_syncObject)
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

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                IntBarSensorValue typedData = (IntBarSensorValue)data;
                return JsonSerializer.Serialize(typedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
            }
        }

        protected override byte[] GetBytesData(SensorValueBase data)
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
            int mean = 0;
            try
            {
                mean = (int)(sum / ValuesCount);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return mean;
        }
    }
}
