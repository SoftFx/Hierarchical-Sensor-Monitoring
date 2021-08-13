using HSMCommon.Model.SensorsData;
using HSMDatabase.DatabaseInterface;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Entity;
using HSMSensorDataObjects;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer.Model;
using HSMServer.Registration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.DataLayer
{
    public class DatabaseAdapter : IDatabaseAdapter
    {
        private IPublicAdapter _adapter;
        private IDatabaseCore _database;
        public DatabaseAdapter(IPublicAdapter adapter)
        {
            _adapter = adapter;
            _database = DatabaseCore.GetInstance();
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

        public List<SensorDataEntity> GetAllSensorDataOld(string productName, string path)
        {
            var dataEntities = _adapter.GetSensorHistory(productName, path, -1);
            if (dataEntities == null || !dataEntities.Any())
                return new List<SensorDataEntity>();

            return dataEntities;
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

        public List<ConfigurationObject> GetAllConfigurationObjectsOld()
        {
            var configurationObjects = _adapter.ReadAllConfigurationEntities();
            if (configurationObjects == null || !configurationObjects.Any())
                return new List<ConfigurationObject>();

            return configurationObjects.Select(e => new ConfigurationObject(e)).ToList();
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

        public List<RegistrationTicket> GetAllTicketsOld()
        {
            var ticketEntities = _adapter.ReadAllRegisterTicketEntities();
            if (ticketEntities == null || !ticketEntities.Any())
                return new List<RegistrationTicket>();

            return ticketEntities.Select(e => new RegistrationTicket(e)).ToList();
        }
        
        #endregion

        #region Product

        public void RemoveProduct(string productName)
        {
            _database.RemoveProduct(productName);
        }

        public void AddProduct(Product product)
        {
            var entity = Convert(product);
            _database.AddProduct(entity);
        }

        public void UpdateProduct(Product product)
        {
            AddProduct(product);
        }

        public Product GetProduct(string productName)
        {
            var entity = _database.GetProduct(productName);
            return entity != null ? new Product(entity) : null;
        }

        public List<Product> GetProducts()
        {
            var productEntities = _database.GetAllProducts();
            if (productEntities == null || !productEntities.Any())
                return new List<Product>();

            return productEntities.Select(e => new Product(e)).ToList();
        }

        #endregion

        #region Sensors

        public void RemoveSensor(string productName, string path)
        {
            _database.RemoveSensor(productName, path);
        }

        public void AddSensor(SensorInfo info)
        {
            SensorEntity entity = Convert(info);
            _database.AddSensor(entity);
        }

        public void UpdateSensor(SensorInfo info)
        {
            SensorEntity entity = Convert(info);
            _database.AddSensor(entity);
        }

        public void PutSensorData(SensorDataEntity data, string productName)
        {
            _database.AddSensorValue(data, productName);
        }

        public SensorDataEntity GetLastSensorValue(string productName, string path)
        {
            var value = _database.GetLatestSensorValue(productName, path);
            return value;
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            var sensorEntity = _database.GetSensorInfo(productName, path);
            return sensorEntity != null ? new SensorInfo(sensorEntity) : null;
        }

        public List<SensorHistoryData> GetAllSensorHistory(string productName, string path)
        {
            List<SensorHistoryData> historyDatas = new List<SensorHistoryData>();
            var history = _database.GetAllSensorData(productName, path);
            if (history != null && history.Any())
            {
                historyDatas.AddRange(history.Select(Convert));
            }

            return historyDatas;
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from)
        {
            List<SensorHistoryData> historyDatas = new List<SensorHistoryData>();
            var history = _database.GetSensorData(productName, path, from);
            if (history != null && history.Any())
            {
                historyDatas.AddRange(history.Select(Convert));
            }

            return historyDatas;
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to)
        {
            List<SensorHistoryData> historyDatas = new List<SensorHistoryData>();
            var history = _database.GetSensorData(productName, path, from, to);
            if (history != null && history.Any())
            {
                historyDatas.AddRange(history.Select(Convert));
            }

            return historyDatas;
        }

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            SensorDataEntity entity = _database.GetLatestSensorValue(productName, path);
            return entity != null ? Convert(entity) : null;
        }

        public List<SensorInfo> GetProductSensors(Product product)
        {
            var sensorEntities = _database.GetProductSensors(product.Name);
            if (sensorEntities == null || !sensorEntities.Any())
                return new List<SensorInfo>();
            return sensorEntities.Select(e => new SensorInfo(e)).ToList();
        }

        #endregion

        #region User

        public void AddUser(User user)
        {
            UserEntity entity = Convert(user);
            _database.AddUser(entity);
        }

        public void UpdateUser(User user)
        {
            UserEntity entity = Convert(user);
            _database.AddUser(entity);
        }

        public void RemoveUser(User user)
        {
            UserEntity entity = Convert(user);
            _database.RemoveUser(entity);
        }

        public List<User> GetUsers()
        {
            List<User> users = new List<User>();
            var userEntities = _database.ReadUsers();
            if (userEntities != null && userEntities.Any())
            {
                users.AddRange(userEntities.Select(e => new User(e)));
            }

            return users;
        }

        public List<User> GetUsersPage(int page, int pageSize)
        {
            List<User> users = new List<User>();
            var userEntities = _database.ReadUsersPage(page, pageSize);
            if (userEntities != null && userEntities.Any())
            {
                users.AddRange(userEntities.Select(e => new User(e)));
            }

            return users;
        }

        #endregion

        #region Configuration object

        public ConfigurationObject GetConfigurationObject(string name)
        {
            var entity = _database.ReadConfigurationObject(name);
            return entity != null ? new ConfigurationObject(entity) : null;
        }

        public void WriteConfigurationObject(ConfigurationObject obj)
        {
            var entity = Convert(obj);
            _database.WriteConfigurationObject(entity);
        }

        public void RemoveConfigurationObject(string name)
        {
            _database.RemoveConfigurationObject(name);
        }

        #endregion

        #region Registration ticket

        public RegistrationTicket ReadRegistrationTicket(Guid id)
        {
            var entity = _database.ReadRegistrationTicket(id);
            return entity != null ? new RegistrationTicket(entity) : null;
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            _database.RemoveRegistrationTicket(id);
        }

        public void WriteRegistrationTicket(RegistrationTicket ticket)
        {
            var entity = Convert(ticket);
            _database.WriteRegistrationTicket(entity);
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
