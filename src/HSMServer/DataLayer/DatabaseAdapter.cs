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

        public Product GetProduct(string productName)
        {
            return new Product(_adapter.GetProduct(productName));
        }

        public List<Product> GetProducts()
        {
            return _adapter.GetAllProducts().Select(e => new Product(e)).ToList();
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
            return new SensorInfo(_adapter.GetSensor(productName, path));
        }

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, long n)
        {
            return _adapter.GetSensorHistory(productName, path, n).Select(Convert).ToList();
        }

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            return Convert(_adapter.GetOneValueSensorValue(productName, path));
        }

        #endregion

        #region Users

        public void AddUser(User user)
        {
            var entity = Convert(user);
            _adapter.AddUser(entity);
        }

        public void RemoveUser(User user)
        {
            var entity = Convert(user);
            _adapter.RemoveUser(entity);
        }

        public List<User> GetUsers()
        {
            return _adapter.GetUsers().Select(u => new User(u)).ToList();
        }

        public List<User> GetUsersPage(int page, int pageSize)
        {
            return _adapter.GetUsersPage(page, pageSize).Select(u => new User(u)).ToList();
        }

        #endregion

        #region Configuration

        public ConfigurationObject GetConfigurationObject(string name)
        {
            return new ConfigurationObject(_adapter.ReadConfigurationObject(name));
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
            return new RegistrationTicket(_adapter.ReadRegistrationTicket(id));
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
            result.ExtraKeys = new List<ExtraKeyEntity>();
            result.ExtraKeys.AddRange(product.ExtraKeys.Select(Convert));
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
            result.ProductsRoles = user.ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Key, (byte)r.Value)).ToList();
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
