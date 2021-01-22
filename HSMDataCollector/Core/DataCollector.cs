using System;
using System.Collections.Generic;
using System.Text;
using HSMDataCollector.Base;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _connectionAddress;
        private readonly List<ISensor> _sensors;
        private object _syncRoot = new object();
        public DataCollector(string productKey, string address, int port)
        {
            _connectionAddress = $"{address}:{port}";
            _productKey = productKey;
            _sensors = new List<ISensor>();
        }
        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public void CheckConnection()
        {
            throw new NotImplementedException();
        }

        public ISensor CreateSensor(string name, string path, SensorType type)
        {
            
        }
    }
}
