using HSMDatabase.DatabaseInterface;
using HSMDatabase.Entity;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer.Model;
using HSMServer.Registration;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;

namespace HSMServer.DataLayer
{
    public class DatabaseAdapter : IDatabaseAdapter
    {
        private readonly IPublicAdapter _adapter;
        public DatabaseAdapter(IPublicAdapter adapter)
        {
            _adapter = adapter;
        }

        #region Product

        public void RemoveProduct(string productName)
        {
            _adapter.RemoveProduct(productName);
        }

        public void AddProduct(Product product)
        {
            var entity = Convert(product);
            _adapter.AddProduct(entity);
        }

        public void UpdateProduct(Product product)
        {
            var entity = Convert(product);
            _adapter.UpdateProduct(entity);
        }

        public Product GetProduct(string productName)
        {
            var productEntity = _adapter.GetProduct(productName);
            return productEntity == null ? null : new Product(productEntity);
        }

        public List<Product> GetProducts()
        {
            var productEntities = _adapter.GetAllProducts();
            if (productEntities == null || !productEntities.Any())
                return new List<Product>();

            return productEntities.Select(e => new Product(e)).ToList();
        }

        #endregion

        #region Sensor

        public void RemoveSensor(string productName, string sensorName)
        {
            _adapter.RemoveSensor(productName, sensorName);
        }

        public void AddSensor(SensorInfo info)
        {
            var entity = Convert(info);
            _adapter.AddSensor(entity);
        }

        public void UpdateSensor(SensorInfo info)
        {
            var entity = Convert(info);
            _adapter.UpdateSensor(entity);
        }

        public void PutSensorData(SensorDataEntity data, string productName)
        {
            _adapter.PutSensorData(data, productName);
        }

        public void PutOneValueSensorData(SensorDataEntity data, string productName)
        {
            _adapter.PutOneValueSensorData(data, productName);
        }

        public SensorDataEntity GetLastSensorValue(string productName, string path)
        {
            return _adapter.GetLastSensorValue(productName, path);
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            var sensorEntity = _adapter.GetSensor(productName, path);
            return sensorEntity == null ? null : new SensorInfo(sensorEntity);
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, long n)
        {
            var dataEntities = _adapter.GetSensorHistory(productName, path, n);
            if (dataEntities == null || !dataEntities.Any())
                return new List<SensorHistoryData>();

            return dataEntities.Select(Convert).ToList();
        }

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            var dataEntity = _adapter.GetOneValueSensorValue(productName, path);
            return dataEntity == null ? null : Convert(dataEntity);
        }

        public List<SensorInfo> GetProductSensors(Product product)
        {
            var sensorEntities = _adapter.GetProductSensors(product.Name);
            if(sensorEntities == null || !sensorEntities.Any())
                return new List<SensorInfo>();
            return sensorEntities.Select(e => new SensorInfo(e)).ToList();
        }

        #endregion

        #region Users

        public void AddUser(User user)
        {
            var entity = Convert(user);
            _adapter.AddUser(entity);
        }

        public void UpdateUser(User user)
        {
            var entity = Convert(user);
            _adapter.UpdateUser(entity);
        }

        public void RemoveUser(User user)
        {
            var entity = Convert(user);
            _adapter.RemoveUser(entity);
        }

        public List<User> GetUsers()
        {
            var userEntities = _adapter.GetUsers();
            if(userEntities == null || !userEntities.Any())
                return new List<User>();
            return userEntities.Select(u => new User(u)).ToList();
        }

        public List<User> GetUsersPage(int page, int pageSize)
        {
            var userEntities = _adapter.GetUsersPage(page, pageSize);
            if (userEntities == null || !userEntities.Any())
                return new List<User>();
            return userEntities.Select(u => new User(u)).ToList();
        }

        #endregion

        #region Configuration

        public ConfigurationObject GetConfigurationObject(string name)
        {
            var configurationEntity = _adapter.ReadConfigurationObject(name);
            return configurationEntity == null ? null : new ConfigurationObject(configurationEntity);
        }

        public void WriteConfigurationObject(ConfigurationObject obj)
        {
            var entity = Convert(obj);
            _adapter.WriteConfigurationObject(entity);
        }

        #endregion

        #region Register tickets

        public RegistrationTicket ReadRegistrationTicket(Guid id)
        {
            var ticketEntity = _adapter.ReadRegistrationTicket(id);
            return ticketEntity == null ? null : new RegistrationTicket(ticketEntity);
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            _adapter.RemoveRegistrationTicket(id);
        }

        public void WriteRegistrationTicket(RegistrationTicket ticket)
        {
            _adapter.WriteRegistrationTicket(Convert(ticket));
        }

        #endregion


        #region Convert objects

        private ProductEntity Convert(Product product)
        {
            ProductEntity result = new ProductEntity();
            result.Name = product.Name;
            result.Key = product.Key;
            result.DateAdded = product.DateAdded;
            if (product.ExtraKeys != null && product.ExtraKeys.Any())
            {
                result.ExtraKeys = product.ExtraKeys.Select(Convert).ToList();
            }
            return result;
        }

        private ExtraKeyEntity Convert(ExtraProductKey key)
        {
            ExtraKeyEntity result = new ExtraKeyEntity();
            result.Key = key.Key;
            result.Name = key.Name;
            return result;
        }

        private SensorEntity Convert(SensorInfo info)
        {
            SensorEntity result = new SensorEntity();
            result.Description = info.Description;
            result.Path = info.Path;
            result.ProductName = info.ProductName;
            result.SensorName = info.SensorName;
            return result;
        }

        private UserEntity Convert(User user)
        {
            UserEntity result = new UserEntity();
            result.UserName = user.UserName;
            result.Password = user.Password;
            result.CertificateThumbprint = user.CertificateThumbprint;
            result.CertificateFileName = user.CertificateFileName;
            result.Id = user.Id;
            result.IsAdmin = user.IsAdmin;
            if (user.ProductsRoles != null && user.ProductsRoles.Any())
            {
                result.ProductsRoles = user.ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Key, (byte)r.Value)).ToList();
            }
            return result;
        }

        private ConfigurationEntity Convert(ConfigurationObject obj)
        {
            ConfigurationEntity result = new ConfigurationEntity();
            result.Value = obj.Value;
            result.Name = obj.Name;
            return result;
        }

        private RegisterTicketEntity Convert(RegistrationTicket ticket)
        {
            RegisterTicketEntity result = new RegisterTicketEntity();
            result.Role = ticket.Role;
            result.ExpirationDate = ticket.ExpirationDate;
            result.Id = ticket.Id;
            result.ProductKey = ticket.ProductKey;
            return result;
        }

        private SensorHistoryData Convert(SensorDataEntity entity)
        {
            SensorHistoryData result = new SensorHistoryData();
            result.SensorType = (SensorType) entity.DataType;
            result.TypedData = entity.TypedData;
            result.Time = entity.Time;
            return result;
        }
        #endregion
    }
}
