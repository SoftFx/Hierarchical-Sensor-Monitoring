using HSMDatabase.Entity;
using System;
using System.Collections.Generic;
using HSMDatabase.DatabaseInterface;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class PublicAdapter : IPublicAdapter
    {
        private readonly IDatabaseWorker _databaseWorker;
        public PublicAdapter(IDatabaseWorker databaseWorker)
        {
            _databaseWorker = databaseWorker;
        }

        #region Product

        public void RemoveProduct(string productName)
        {
            var sensorsList = _databaseWorker.GetSensorsList(productName);
            _databaseWorker.RemoveProductInfo(productName);
            _databaseWorker.RemoveProductFromList(productName);
            _databaseWorker.RemoveSensorsList(productName);
            foreach (var sensor in sensorsList)
            {
                _databaseWorker.RemoveSensor(productName, sensor);
            }
        }

        public void AddProduct(ProductEntity productEntity)
        {
            _databaseWorker.PutProductInfo(productEntity);
            _databaseWorker.AddProductToList(productEntity.Name);
        }

        public ProductEntity GetProduct(string productName)
        {
            return _databaseWorker.GetProductInfo(productName);
        }

        public List<ProductEntity> GetAllProducts()
        {
            List<ProductEntity> result = new List<ProductEntity>();
            var productsList = _databaseWorker.GetProductsList();
            foreach (var productName in productsList)
            {
                var product = _databaseWorker.GetProductInfo(productName);
                if (product != null)
                    result.Add(product);
            }

            return result;
        }

        #endregion

        #region Sensor

        public void RemoveSensor(string productName, string path)
        {
            _databaseWorker.RemoveSensor(productName, path);
            _databaseWorker.RemoveSensorFromList(productName, path);
            _databaseWorker.RemoveSensorValues(productName, path);
        }
        public void RemoveSensor(SensorEntity sensorEntity)
        {
            RemoveSensor(sensorEntity.ProductName, sensorEntity.Path);
        }

        public void AddSensor(SensorEntity sensorEntity)
        {
            _databaseWorker.AddSensor(sensorEntity);
        }

        public void PutSensorData(SensorDataEntity data, string productName)
        {
            _databaseWorker.WriteSensorData(data, productName);
        }

        public void PutOneValueSensorData(SensorDataEntity data, string productName)
        {
            _databaseWorker.WriteOneValueSensorData(data, productName);
        }

        public SensorDataEntity GetLastSensorValue(string productName, string path)
        {
            return _databaseWorker.GetLastSensorValue(productName, path);
        }

        public SensorEntity GetSensor(string productName, string path)
        {
            return _databaseWorker.GetSensorInfo(productName, path);
        }

        #endregion

        #region Users

        public void AddUser(UserEntity user)
        {
            _databaseWorker.AddUser(user);
        }

        public void RemoveUser(UserEntity user)
        {
            _databaseWorker.RemoveUser(user);
        }

        public List<UserEntity> GetUsers()
        {
            return _databaseWorker.ReadUsers();
        }

        public List<UserEntity> GetUsersPage(int page, int pageSize)
        {
            return _databaseWorker.ReadUsersPage(page, pageSize);

        }

        #endregion


        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            return _databaseWorker.ReadConfigurationObject(name);
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            _databaseWorker.WriteConfigurationObject(obj);
        }

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            return _databaseWorker.ReadRegistrationTicket(id);
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            _databaseWorker.RemoveRegistrationTicket(id);
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            _databaseWorker.WriteRegistrationTicket(ticket);
        }
    }
}
