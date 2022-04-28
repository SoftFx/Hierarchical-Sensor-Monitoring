using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMSensorDataObjects;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.DataLayer
{
    public class DatabaseAdapter : IDatabaseAdapter
    {
        private readonly DatabaseCore _database;


        public DatabaseAdapter(IDatabaseSettings dbSettings = null)
        {
            _database = new DatabaseCore(dbSettings ?? new DatabaseSettings());
        }


        #region Size

        public long GetDatabaseSize() => _database.GetDatabaseSize();

        public long GetMonitoringDataSize() => _database.GetMonitoringDataSize();

        public long GetEnvironmentDatabaseSize() =>
            _database.GetEnvironmentDatabaseSize();

        #endregion

        #region Product

        public void RemoveProduct(string productName) =>
            _database.RemoveProduct(productName);

        public void RemoveProductNew(string id) =>
            _database.RemoveProductNew(id);

        public void AddProduct(Product product)
        {
            var entity = ConvertProductToEntity(product);
            _database.AddProduct(entity);
        }

        public void AddProductNew(Product product)
        {
            var entity = ConvertProductToEntity(product);
            _database.AddProductNew(entity);
        }

        public void AddProduct(ProductEntity product) => _database.AddProduct(product);

        public void AddProductNew(ProductEntity entity) => _database.AddProductNew(entity);

        public void UpdateProduct(Product product) => AddProduct(product);

        public void UpdateProductNew(Product product) => AddProductNew(product);

        public void UpdateProduct(ProductEntity product) => _database.AddProduct(product);

        public void UpdateProductNew(ProductEntity entity) => _database.AddProductNew(entity);

        public Product GetProduct(string productName)
        {
            var entity = _database.GetProduct(productName);
            return entity != null ? new Product(entity) : null;
        }

        public Product GetProductNew(string id)
        {
            var entity = _database.GetProductNew(id);
            return entity != null ? new Product(entity) : null;
        }

        public List<Product> GetProducts() =>
            GetAllProducts()?.Select(e => new Product(e))?.ToList() ?? new List<Product>();

        public List<ProductEntity> GetAllProducts() 
        {
            var dictionary = new Dictionary<string, ProductEntity>();
            
            var oldEntities = _database.GetAllProductsOld();
            var convertedEntities = _database.GetAllProductsNew();
            if (oldEntities != null && oldEntities.Count > 0)
            {
                foreach (var oldEntity in oldEntities)
                {
                    var entity = EntityConverter.ConvertProductEntity(oldEntity);
                    dictionary.Add(entity.DisplayName, entity);
                }
            }

            if (convertedEntities != null && convertedEntities.Count > 0)
            {
                foreach (var convertedEntity in convertedEntities)
                {
                    var entity = EntityConverter.ConvertProductEntity(convertedEntity);
                    if (dictionary.ContainsKey(entity.DisplayName))
                        dictionary.Remove(entity.DisplayName);
                }
            }

            if (dictionary.Count == 0) return null;

            return dictionary.Values.ToList();
        }

        #endregion

        #region Sensors

        public void RemoveSensor(string productName, string path) =>
            _database.RemoveSensor(productName, path);

        public void RemoveSensorWithMetadata(string productName, string path) =>
            _database.RemoveSensorWithMetadata(productName, path);

        public void AddSensor(SensorInfo info)
        {
            SensorEntity entity = ConvertSensorInfoToEntity(info);
            _database.AddSensor(entity);
        }

        public void AddSensor(SensorEntity sensor) => _database.AddSensor(sensor);

        public void UpdateSensor(SensorInfo info)
        {
            SensorEntity entity = ConvertSensorInfoToEntity(info);
            _database.AddSensor(entity);
        }

        public void UpdateSensor(SensorEntity sensor) => _database.AddSensor(sensor);

        public void PutSensorData(SensorDataEntity data, string productName) =>
            _database.AddSensorValue(data, productName);

        public SensorDataEntity GetLastSensorValue(string productName, string path) =>
            _database.GetLatestSensorValue(productName, path);

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            var sensorEntity = _database.GetSensorInfo(productName, path);
            return sensorEntity != null ? new SensorInfo(sensorEntity) : null;
        }

        public List<SensorHistoryData> GetAllSensorHistory(string productName, string path) =>
            GetSensorHistoryDatas(_database.GetAllSensorData(productName, path));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from) =>
            GetSensorHistoryDatas(_database.GetSensorData(productName, path, from));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to) =>
            GetSensorHistoryDatas(_database.GetSensorData(productName, path, from, to));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, int n) =>
            GetSensorHistoryDatas(_database.GetSensorData(productName, path, n));

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            SensorDataEntity entity = _database.GetLatestSensorValue(productName, path);
            return entity != null ? ConvertSensorDataEntityToHistoryData(entity) : null;
        }

        public List<SensorInfo> GetProductSensors(Product product) =>
            _database.GetProductSensors(product.DisplayName)?.Select(e => new SensorInfo(e))?.ToList() ?? new List<SensorInfo>();

        private List<SensorHistoryData> GetSensorHistoryDatas(List<SensorDataEntity> history)
        {
            var historyCount = history?.Count ?? 0;

            List<SensorHistoryData> historyDatas = new List<SensorHistoryData>(historyCount);
            if (historyCount != 0)
                historyDatas.AddRange(history.Select(ConvertSensorDataEntityToHistoryData));

            return historyDatas;
        }

        public List<SensorEntity> GetAllSensors()
        {
            var oldEntities = _database.GetAllSensors();
            if (oldEntities == null || oldEntities.Count == 0) 
                return new List<SensorEntity>();

            foreach (var oldEntity in oldEntities)
            {
                if (!string.IsNullOrEmpty(oldEntity.Id) 
                    && !string.Equals(oldEntity.Id, Guid.Empty.ToString())) 
                    continue;

                oldEntity.Id = Guid.NewGuid().ToString();
                oldEntity.ProductId = Guid.Empty.ToString();
                oldEntity.IsConverted = true;
            }

            return oldEntities;
        }

        #endregion

        #region User

        public void AddUser(User user)
        {
            UserEntity entity = ConvertUserToEntity(user);
            _database.AddUser(entity);
        }

        public void UpdateUser(User user)
        {
            UserEntity entity = ConvertUserToEntity(user);
            _database.AddUser(entity);
        }

        public void RemoveUser(User user)
        {
            UserEntity entity = ConvertUserToEntity(user);
            _database.RemoveUser(entity);
        }

        public List<User> GetUsers() => GetUsers(_database.ReadUsers());

        public List<User> GetUsersPage(int page, int pageSize) =>
            GetUsers(_database.ReadUsersPage(page, pageSize));

        private static List<User> GetUsers(List<UserEntity> userEntities)
        {
            var userEntitiesCount = userEntities?.Count ?? 0;
            var users = new List<User>(userEntitiesCount);

            if (userEntitiesCount != 0)
                users.AddRange(userEntities.Select(e => new User(e)));

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
            var entity = ConvertConfigurationObjectToEntity(obj);
            _database.WriteConfigurationObject(entity);
        }

        public void RemoveConfigurationObject(string name) => _database.RemoveConfigurationObject(name);

        #endregion

        #region Registration ticket

        public RegistrationTicket ReadRegistrationTicket(Guid id)
        {
            var entity = _database.ReadRegistrationTicket(id);
            return entity != null ? new RegistrationTicket(entity) : null;
        }

        public void RemoveRegistrationTicket(Guid id) => _database.RemoveRegistrationTicket(id);

        public void WriteRegistrationTicket(RegistrationTicket ticket)
        {
            var entity = ConvertRegistrationTicketToEntity(ticket);
            _database.WriteRegistrationTicket(entity);
        }

        #endregion

        #region Convert objects

        private SensorHistoryData ConvertSensorDataEntityToHistoryData(SensorDataEntity entity) =>
            new()
            {
                SensorType = (SensorType)entity.DataType,
                TypedData = entity.TypedData,
                Time = entity.Time,
                OriginalFileSensorContentSize = entity.OriginalFileSensorContentSize,
            };

        private static ProductEntity ConvertProductToEntity(Product product) =>
            new()
            {
                DisplayName = product.DisplayName,
                Id = product.Id,
                CreationDate = product.CreationDate.Ticks
            };

        private static SensorEntity ConvertSensorInfoToEntity(SensorInfo info) =>
            new()
            {
                Description = info.Description,
                Path = info.Path,
                ProductName = info.ProductName,
                SensorName = info.SensorName,
                SensorType = (int)info.SensorType,
                Unit = info.Unit,
                ExpectedUpdateIntervalTicks = info.ExpectedUpdateInterval.Ticks,
                ValidationParameters = info.ValidationParameters?.Select(ConvertSensorValidationParameterToEntity)?.ToList(),
            };

        private static ValidationParameterEntity ConvertSensorValidationParameterToEntity(SensorValidationParameter validationParameter) =>
            new()
            {
                ValidationValue = validationParameter.ValidationValue,
                ParameterType = (int)validationParameter.ValidationType
            };

        private static UserEntity ConvertUserToEntity(User user) =>
            new()
            {
                UserName = user.UserName,
                Password = user.Password,
                CertificateThumbprint = user.CertificateThumbprint,
                CertificateFileName = user.CertificateFileName,
                Id = user.Id,
                IsAdmin = user.IsAdmin,
                ProductsRoles = user.ProductsRoles?.Select(r => new KeyValuePair<string, byte>(r.Key, (byte)r.Value))?.ToList(),
            };

        private static ConfigurationEntity ConvertConfigurationObjectToEntity(ConfigurationObject obj) =>
            new()
            {
                Value = obj.Value,
                Name = obj.Name,
            };

        private static RegisterTicketEntity ConvertRegistrationTicketToEntity(RegistrationTicket ticket) =>
            new()
            {
                Role = ticket.Role,
                ExpirationDate = ticket.ExpirationDate,
                Id = ticket.Id,
                ProductKey = ticket.ProductKey,
            };

        #endregion

        public void Dispose() => _database.Dispose();
    }
}
