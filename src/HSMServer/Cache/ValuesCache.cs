using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSMCommon.Model.SensorsData;

namespace HSMServer.Cache
{
    internal class ValuesCache : IValuesCache
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, SortedList<string, SensorData>> _productSensorDictionary;

        public ValuesCache()
        {
            _productSensorDictionary = new Dictionary<string, SortedList<string, SensorData>>();
        }
        public void AddValue(string productName, SensorData sensorData)
        {
            //var dict = _productSensorDictionary.GetOrAdd(productName, new ConcurrentDictionary<string, SensorData>());
            //var newVal = dict.AddOrUpdate(sensorData.Path, sensorData, (key, oldValue) => sensorData);
            lock (_syncRoot)
            {
                SortedList<string, SensorData> list;

                if (!_productSensorDictionary.ContainsKey(productName))
                {
                    list = new SortedList<string, SensorData>();
                    _productSensorDictionary[productName] = list;
                }
                else
                {
                    list = _productSensorDictionary[productName];
                }

                list[sensorData.Path] = sensorData.Clone();
            }
        }

        public List<SensorData> GetValues(List<string> products)
        {
            List<SensorData> result = new List<SensorData>();
            foreach (var productName in products)
            {
                lock (_syncRoot)
                {
                    bool doSensorsExist = _productSensorDictionary.TryGetValue(productName, out var data);
                    if (doSensorsExist)
                    {
                        result.AddRange(data.Values.Select(v => v.Clone()));
                    }
                }
            }
            //foreach (var product in products)
            //{
            //    bool res = _productSensorDictionary.TryGetValue(product, out var dict);
            //    if (res)
            //    {
            //        result.AddRange(dict.Values);
            //    }
            //}

            return result;
        }

        public void RemoveSensorValue(string productName, string path)
        {
            lock (_syncRoot)
            {
                var productsList = _productSensorDictionary[productName];
                productsList.Remove(path);
            }
            //bool res = _productSensorDictionary.TryGetValue(productName, out var dict);
            //if (res)
            //{
            //    bool removeRes = dict.TryRemove(path, out var data);
            //    if (!removeRes)
            //    {
            //        //TODO: throw exception
            //    }
            //}
        }

        public SensorData GetValue(string productName, string path)
        {
            //bool res = _productSensorDictionary.TryGetValue(productName, out var dict);
            //if (!res)
            //    return null;

            //bool getValueRes = dict.TryGetValue(path, out var data);
            //return data;
            throw new NotImplementedException();
        }
    }
}
