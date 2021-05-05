using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.DataLayer.Model;

namespace HSMServer.DataLayer
{
    public class ImprovedLevelDBDatabase : IDatabaseClass
    {
        public ImprovedLevelDBDatabase()
        {

        }

        public void AddProductToList(string productName)
        {
            throw new NotImplementedException();
        }

        public List<string> GetProductsList()
        {
            throw new NotImplementedException();
        }

        public Product GetProductInfo(string productName)
        {
            throw new NotImplementedException();
        }

        public void PutProductInfo(Product product)
        {
            throw new NotImplementedException();
        }

        public void RemoveProductInfo(string name)
        {
            throw new NotImplementedException();
        }

        public void RemoveProductFromList(string name)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensor(SensorInfo info)
        {
            throw new NotImplementedException();
        }

        public void AddSensor(SensorInfo info)
        {
            throw new NotImplementedException();
        }

        public void WriteSensorData(SensorDataObject dataObject, string productName)
        {
            throw new NotImplementedException();
        }

        public void WriteOneValueSensorData(SensorDataObject dataObject, string productName)
        {
            throw new NotImplementedException();
        }

        public SensorDataObject GetLastSensorValue(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorDataObject> GetSensorDataHistory(string productName, string path, long n)
        {
            throw new NotImplementedException();
        }

        public List<string> GetSensorsList(string productName)
        {
            throw new NotImplementedException();
        }

        public void AddNewSensorToList(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorFromList(string productName, string sensorName)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
