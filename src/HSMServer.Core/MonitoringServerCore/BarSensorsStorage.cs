using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMServer.Core.MonitoringServerCore
{
    public class BarSensorsStorage : IBarSensorsStorage
    {
        private readonly object _syncObject;
        private readonly Dictionary<(string, string), ExtendedBarSensorData> _lastValues;
        private readonly Timer _checkOutdatedTimer;
        private readonly TimeSpan _checkSpan;
        public BarSensorsStorage()
        {
            _syncObject = new object();
            _lastValues = new Dictionary<(string, string), ExtendedBarSensorData>(new DictionaryComparer());
            _checkSpan = TimeSpan.FromMinutes(1);
            TimeSpan timerSpan = TimeSpan.FromSeconds(20);
            _checkOutdatedTimer = new Timer(CheckOutdatedCallback, null, timerSpan, timerSpan);
        }

        public void Add(IntBarSensorValue value, string product, DateTime timeCollected)
        {
            ExtendedBarSensorData data = new ExtendedBarSensorData()
            {
                ProductName = product,
                TimeCollected = timeCollected,
                ValueType = SensorType.IntegerBarSensor,
                Value = value
            };
            lock (_syncObject)
            {
                _lastValues[(product, value.Path)] = data;
            }
        }

        public void Add(DoubleBarSensorValue value, string product, DateTime timeCollected)
        {
            ExtendedBarSensorData data = new ExtendedBarSensorData()
            {
                ProductName = product,
                TimeCollected = timeCollected,
                ValueType = SensorType.DoubleBarSensor,
                Value = value
            };
            lock (_syncObject)
            {
                _lastValues[(product, value.Path)] = data;
            }
        }

        public void Remove(string product, string path)
        {
            lock (_syncObject)
            {
                _lastValues.Remove((product, path));
            }
        }
        public ExtendedBarSensorData GetLastValue(string product, string path)
        {
            ExtendedBarSensorData result = null;
            (string, string) key = (product, path);
            lock (_syncObject)
            {
                if (_lastValues.ContainsKey(key))
                {
                    result = _lastValues[key];
                }
            }

            return result;
        }

        public List<ExtendedBarSensorData> GetAllLastValues()
        {
            List<ExtendedBarSensorData> result = new List<ExtendedBarSensorData>();
            lock (_syncObject)
            {
                result.AddRange(_lastValues.Values);
            }

            return result;
        }

        public event EventHandler<ExtendedBarSensorData> IncompleteBarOutdated;

        private void CheckOutdatedCallback(object state)
        {
            List<ExtendedBarSensorData> list = new List<ExtendedBarSensorData>();
            DateTime checkDate = DateTime.Now.ToUniversalTime();
            lock (_syncObject)
            {
                foreach (var value in _lastValues)
                {
                    if (checkDate - value.Value.TimeCollected > _checkSpan)
                    {
                        list.Add(value.Value);
                    }
                }
            }

            foreach (var item in list)
            {
                lock (_syncObject)
                {
                    _lastValues.Remove((item.ProductName, item.Value.Path));
                }
                OnIncompleteBarOutdated(item);
            }
        }
        private void OnIncompleteBarOutdated(ExtendedBarSensorData data)
        {
            IncompleteBarOutdated?.Invoke(this, data);
        }


        private class DictionaryComparer : IEqualityComparer<(string, string)>
        {
            public bool Equals((string, string) x, (string, string) y)
            {
                if (x.Item1.Equals(y.Item1))
                {
                    return true;
                }

                return x.Item2.Equals(y.Item2);
            }

            public int GetHashCode((string, string) obj)
            {
                return obj.GetHashCode();
            }
        }


        public void Dispose()
        {
            _checkOutdatedTimer?.Dispose();
            lock (_syncObject)
            {
                foreach (var pair in _lastValues)
                {
                    var data = pair.Value;
                    _lastValues.Remove(pair.Key);
                    OnIncompleteBarOutdated(data);
                }
            }
        }
    }
}
