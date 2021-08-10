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
        private IPublicAdapter _adapter;
        private IDatabaseCore _database;
        public DatabaseAdapter(IPublicAdapter adapter, IDatabaseCore database)
        {
            _adapter = adapter;
            _database = database;
        }

        #region Product Old

        public void RemoveProductOld(string productName)
        {
            _adapter.RemoveProduct(productName);
        }

        public void AddProductOld(Product product)
        {
            var entity = Convert(product);
            _adapter.AddProduct(entity);
        }

        public void UpdateProductOld(Product product)
        {
            var entity = Convert(product);
            _adapter.UpdateProduct(entity);
        }

        public Product GetProductOld(string productName)
        {
            var productEntity = _adapter.GetProduct(productName);
            return productEntity == null ? null : new Product(productEntity);
        }

        public List<Product> GetProductsOld()
        {
            var productEntities = _adapter.GetAllProducts();
            if (productEntities == null || !productEntities.Any())
                return new List<Product>();

            return productEntities.Select(e => new Product(e)).ToList();
        }

        #endregion

        #region Sensor Old

        public void RemoveSensorOld(string productName, string path)
        {
            _adapter.RemoveSensor(productName, path);
        }

        public void AddSensorOld(SensorInfo info)
        {
            var entity = Convert(info);
            _adapter.AddSensor(entity);
        }

        public void UpdateSensorOld(SensorInfo info)
        {
            var entity = Convert(info);
            _adapter.UpdateSensor(entity);
        }

        public void PutSensorDataOld(SensorDataEntity data, string productName)
        {
            _adapter.PutSensorData(data, productName);
        }

        public void PutOneValueSensorDataOld(SensorDataEntity data, string productName)
        {
            _adapter.PutOneValueSensorData(data, productName);
        }

        public SensorDataEntity GetLastSensorValueOld(string productName, string path)
        {
            return _adapter.GetLastSensorValue(productName, path);
        }

        public SensorInfo GetSensorInfoOld(string productName, string path)
        {
            var sensorEntity = _adapter.GetSensor(productName, path);
            return sensorEntity == null ? null : new SensorInfo(sensorEntity);
        }

        public List<SensorHistoryData> GetSensorHistoryOld(string productName, string path, long n)
        {
            var dataEntities = _adapter.GetSensorHistory(productName, path, n);
            if (dataEntities == null || !dataEntities.Any())
                return new List<SensorHistoryData>();

            return dataEntities.Select(Convert).ToList();
        }

        public SensorHistoryData GetOneValueSensorValueOld(string productName, string path)
        {
            var dataEntity = _adapter.GetOneValueSensorValue(productName, path);
            return dataEntity == null ? null : Convert(dataEntity);
        }

        public List<SensorInfo> GetProductSensorsOld(Product product)
        {
            var sensorEntities = _adapter.GetProductSensors(product.Name);
            if(sensorEntities == null || !sensorEntities.Any())
                return new List<SensorInfo>();
            return sensorEntities.Select(e => new SensorInfo(e)).ToList();
        }

        #endregion

        #region Users Old

        public void AddUserOld(User user)
        {
            var entity = Convert(user);
            _adapter.AddUser(entity);
        }

        public void UpdateUserOld(User user)
        {
            var entity = Convert(user);
            _adapter.UpdateUser(entity);
        }

        public void RemoveUserOld(User user)
        {
            var entity = Convert(user);
            _adapter.RemoveUser(entity);
        }

        public List<User> GetUsersOld()
        {
            var userEntities = _adapter.GetUsers();
            if(userEntities == null || !userEntities.Any())
                return new List<User>();
            return userEntities.Select(u => new User(u)).ToList();
        }

        public List<User> GetUsersPageOld(int page, int pageSize)
        {
            var userEntities = _adapter.GetUsersPage(page, pageSize);
            if (userEntities == null || !userEntities.Any())
                return new List<User>();
            return userEntities.Select(u => new User(u)).ToList();
        }

        #endregion

        #region Configuration Old

        public ConfigurationObject GetConfigurationObjectOld(string name)
        {
            var configurationEntity = _adapter.ReadConfigurationObject(name);
            return configurationEntity == null ? null : new ConfigurationObject(configurationEntity);
        }

        public void WriteConfigurationObjectOld(ConfigurationObject obj)
        {
            var entity = Convert(obj);
            _adapter.WriteConfigurationObject(entity);
        }

        public void RemoveConfigurationObjectOld(string name)
        {
            _adapter.RemoveConfigurationObject(name);
        }

        #endregion

        #region Register tickets Old

        public RegistrationTicket ReadRegistrationTicketOld(Guid id)
        {
            var ticketEntity = _adapter.ReadRegistrationTicket(id);
            return ticketEntity == null ? null : new RegistrationTicket(ticketEntity);
        }

        public void RemoveRegistrationTicketOld(Guid id)
        {
            _adapter.RemoveRegistrationTicket(id);
        }

        public void WriteRegistrationTicketOld(RegistrationTicket ticket)
        {
            _adapter.WriteRegistrationTicket(Convert(ticket));
        }

        #endregion

        #region Product

        public void RemoveProduct(string productName)
        {
            throw new NotImplementedException();
        }

        public void AddProduct(Product product)
        {
            throw new NotImplementedException();
        }

        public void UpdateProduct(Product product)
        {
            throw new NotImplementedException();
        }

        public Product GetProduct(string productName)
        {
            throw new NotImplementedException();
        }

        public List<Product> GetProducts()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Sensors

        public void RemoveSensor(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void AddSensor(SensorInfo info)
        {
            throw new NotImplementedException();
        }

        public void UpdateSensor(SensorInfo info)
        {
            throw new NotImplementedException();
        }

        public void PutSensorData(SensorDataEntity data, string productName)
        {
            throw new NotImplementedException();
        }

        public SensorDataEntity GetLastSensorValue(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorHistoryData> GetAllSensorHistory(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime @from)
        {
            throw new NotImplementedException();
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime @from, DateTime to)
        {
            throw new NotImplementedException();
        }

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorInfo> GetProductSensors(Product product)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region User

        public void AddUser(User user)
        {
            throw new NotImplementedException();
        }

        public void UpdateUser(User user)
        {
            throw new NotImplementedException();
        }

        public void RemoveUser(User user)
        {
            throw new NotImplementedException();
        }

        public List<User> GetUsers()
        {
            throw new NotImplementedException();
        }

        public List<User> GetUsersPage(int page, int pageSize)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Configuration object

        public ConfigurationObject GetConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        public void WriteConfigurationObject(ConfigurationObject obj)
        {
            throw new NotImplementedException();
        }

        public void RemoveConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Registration ticket

        public RegistrationTicket ReadRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void WriteRegistrationTicket(RegistrationTicket ticket)
        {
            throw new NotImplementedException();
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

        //public void Dispose()
        //{
        //    _adapter?.Dispose();
        //    _adapter = null;
        //}
    }
}
