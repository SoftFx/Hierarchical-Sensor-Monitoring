using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.Cache
{
    internal class ValuesCache : IValuesCache
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, Dictionary<string, SensorData>> _productSensorDictionary;

        public ValuesCache()
        {
            _productSensorDictionary = new Dictionary<string, Dictionary<string, SensorData>>();
        }
        public void AddValue(string productName, SensorData sensorData)
        {
            lock (_syncRoot)
            {
                Dictionary<string, SensorData> dict;
                if (!_productSensorDictionary.ContainsKey(productName))
                {
                    dict = new Dictionary<string, SensorData>();
                    _productSensorDictionary[productName] = dict;
                }
                else
                {
                    dict = _productSensorDictionary[productName];
                }

                dict[sensorData.Path] = sensorData;
            }
        }

        public List<SensorData> GetValues(List<string> products)
        {
            List<SensorData> result = new List<SensorData>();
            foreach (var productName in products)
            {
                lock (_syncRoot)
                {
                    result.AddRange(_productSensorDictionary[productName].Values);
                }
            }

            return result;
        }

        public void RemoveSensorValue(string productName, string path)
        {
            lock (_syncRoot)
            {
                var productDictionary = _productSensorDictionary[productName];
                productDictionary.Remove(path);
            }
        }
    }
}
