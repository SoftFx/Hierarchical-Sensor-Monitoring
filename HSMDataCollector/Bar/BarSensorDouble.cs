using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDSensorDataObjects;
using HSMSensorDataObjects;

namespace HSMDataCollector.Bar
{
    public class BarSensorDouble : BarSensorBase<double>, IDoubleBarSensor
    {
        public BarSensorDouble(string path, string productKey, string serverAddress, IValuesQueue queue, int collectPeriod = 30000)
            : base(path, productKey, $"{serverAddress}/doubleBar", queue, collectPeriod)
        {
            Max = double.MinValue;
            Min = double.MaxValue;
        }

        protected override void SendDataTimer(object state)
        {
            DoubleBarSensorValue dataObject = GetDataObject();
            string serializedValue = GetStringData(dataObject);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.DoubleBarSensor;
            SendData(commonValue);
        }

        public void AddValue(double value)
        {
            lock (_syncObject)
            {
                if (value > Max)
                {
                    Max = value;
                }

                if (value < Min)
                {
                    Min = value;
                }

                ++ValuesCount;
                ValuesList.Add(value);
            }
        }

        private DoubleBarSensorValue GetDataObject()
        {
            DoubleBarSensorValue result = new DoubleBarSensorValue();
            lock (_syncObject)
            {
                result.Max = Max;
                Max = double.MinValue;
                result.Min = Min;
                Min = double.MaxValue;
                result.Mean = CountMean();
                result.Count = ValuesCount;
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
                DoubleBarSensorValue typedData = (DoubleBarSensorValue)data;
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
                DoubleBarSensorValue typedData = (DoubleBarSensorValue)data;
                string convertedString = JsonSerializer.Serialize(typedData);
                return Encoding.UTF8.GetBytes(convertedString);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new byte[1];
            }
            
        }

        private double CountMean()
        {
            double sum = ValuesList.Sum();
            double mean = 0.0;
            try
            {
                mean = sum / ValuesCount;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return mean;
        }
    }
}
