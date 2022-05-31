using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal class EnvironmentDatabaseWorker : IEnvironmentDatabase
    {
        private readonly LevelDBDatabaseAdapter _database;
        private readonly Logger _logger;

        private readonly byte[] _accessKeyListKey;
        private readonly byte[] _productListKey;

        public EnvironmentDatabaseWorker(string name)
        {
            _database = new LevelDBDatabaseAdapter(name);
            _logger = LogManager.GetCurrentClassLogger();

            var key = PrefixConstants.GetAccessKeyListKey();
            _accessKeyListKey = Encoding.UTF8.GetBytes(key);

            key = PrefixConstants.GetProductsListKey();
            _productListKey = Encoding.UTF8.GetBytes(key);
        }

        #region Products

        //ToDo: use like ID
        public void AddProductToList(string productName)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out var value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))   
                    : new List<string>();

                if (!currentList.Contains(productName))
                    currentList.Add(productName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);

                _database.Put(_productListKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        public List<string> GetProductsList()
        {
            var result = new List<string>();
            try
            {
                var products = _database.TryRead(_productListKey, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                result.AddRange(products);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get products list");
            }

            return result;
        }

        public ProductEntity GetProduct(string id)
        {
            var bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<ProductEntity>(Encoding.UTF8.GetString(value)) : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for product {id}");
            }

            return null;
        }

        public void PutProduct(ProductEntity product)
        {
            var bytesKey = Encoding.UTF8.GetBytes(product.Id);
            var stringData = JsonSerializer.Serialize(product);
            var bytesValue = Encoding.UTF8.GetBytes(stringData);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put product info for {product.Id}");
            }
        }

        public void RemoveProduct(string id)
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove info for product {id}");
            }
        }

        //ToDo: use like ID
        public void RemoveProductFromList(string productName)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out byte[] value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                currentList.Remove(productName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(_productListKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        #endregion

        #region AccessKey

        public void AddAccessKeyToList(string id) 
        {
            try
            {
                var currentList = GetAccessKeyList();
                if (!currentList.Contains(id))
                    currentList.Add(id);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(_accessKeyListKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add AccessKey {id} to list");
            }
        }

        public List<string> GetAccessKeyList()
        {
            var result = new List<string>();
            try
            {
                var keys = _database.TryRead(_accessKeyListKey, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                result.AddRange(keys);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get AccessKeys list");
            }

            return result;
        }

        public void RemoveAccessKeyFromList(string id)
        {
            try
            {
                var currentList = GetAccessKeyList();
                currentList.Remove(id);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(_accessKeyListKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove AccessKey {id} from list");
            }
        }

        public void AddAccessKey(AccessKeyEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);
            var stringData = JsonSerializer.Serialize(entity);
            var bytesValue = Encoding.UTF8.GetBytes(stringData);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put AccessKey for {entity.Id}");
            }
        }

        public void RemoveAccessKey(string id) 
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove AccessKey by {id}");
            }
        }

        public AccessKeyEntity GetAccessKey(string id)
        {
            var bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<AccessKeyEntity>(Encoding.UTF8.GetString(value)) : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read AccessKey by {id}");
            }

            return null;
        }

        #endregion

        #region Sensors

        public void RemoveSensor(string productName, string path)
        {
            var key = PrefixConstants.GetSensorInfoKey(productName, path);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor info for {productName}/{path}");
            }
        }

        public void AddSensor(SensorEntity info)
        {
            var key = PrefixConstants.GetSensorInfoKey(info.ProductName, info.Path);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            string stringValue = JsonSerializer.Serialize(info);
            byte[] bytesValue = Encoding.UTF8.GetBytes(stringValue);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put sensor info for {info.ProductName}/{info.Path}");
            }
        }

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            string key = PrefixConstants.GetSensorInfoKey(productName, path);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value) 
                    ? JsonSerializer.Deserialize<SensorEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for sensor {productName}/{path}");
            }

            return null;
        }

        public List<SensorEntity> GetSensorsInfo()
        {
            var key = PrefixConstants.GetSensorsInfoReadKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            var result = new List<SensorEntity>();
            try
            {
                var values = _database.GetAllStartingWith(bytesKey);

                foreach(var value in values)
                {
                    result.Add(JsonSerializer.Deserialize<SensorEntity>(Encoding.UTF8.GetString(value)));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get sensors list");
            }

            return result;
        }

        #endregion

        #region User

        public void AddUser(UserEntity user)
        {
            var userKey = PrefixConstants.GetUniqueUserKey(user.UserName);
            var keyBytes = Encoding.UTF8.GetBytes(userKey);
            var stringValue = JsonSerializer.Serialize(user);
            var valueBytes = Encoding.UTF8.GetBytes(stringValue);
            try
            {
                _database.Put(keyBytes, valueBytes);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to save user {user.UserName}");
            }
        }

        public List<UserEntity> ReadUsers()
        {
            var key = PrefixConstants.GetUsersReadKey();
            var keyBytes = Encoding.UTF8.GetBytes(key);
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                List<byte[]> values = _database.GetAllStartingWith(keyBytes);
                foreach (var value in values)
                {
                    try
                    {
                        users.Add(JsonSerializer.Deserialize<UserEntity>(Encoding.UTF8.GetString(value)));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to UserEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read users!");
            }

            return users;
        }

        public void RemoveUser(UserEntity user)
        {
            var userKey = PrefixConstants.GetUniqueUserKey(user.UserName);
            var keyBytes = Encoding.UTF8.GetBytes(userKey);
            try
            {
                _database.Delete(keyBytes);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to delete user '{user.UserName}'");
            }
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            var key = PrefixConstants.GetUsersReadKey();
            var keyBytes = Encoding.UTF8.GetBytes(key);
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                List<byte[]> values = _database.GetPageStartingWith(keyBytes, page, pageSize);
                foreach (var value in values)
                {
                    try
                    {
                        users.Add(JsonSerializer.Deserialize<UserEntity>(Encoding.UTF8.GetString(value)));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to UserEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read users!");
            }

            return users;
        }

        #endregion

        #region Configuration objects

        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            var key = PrefixConstants.GetUniqueConfigurationObjectKey(name);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<ConfigurationEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read configuration object {name}");
            }

            return null;
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            var key = PrefixConstants.GetUniqueConfigurationObjectKey(obj.Name);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            string stringValue = JsonSerializer.Serialize(obj);
            byte[] bytesValue = Encoding.UTF8.GetBytes(stringValue);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write configuration object {obj.Name}");
            }
        }

        public void RemoveConfigurationObject(string name)
        {
            var key = PrefixConstants.GetUniqueConfigurationObjectKey(name);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write configuration object {name}");
            }
        }

        #endregion

        #region Registration ticket

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            var key = PrefixConstants.GetRegistrationTicketKey(id);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value) 
                    ? JsonSerializer.Deserialize<RegisterTicketEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read registration ticket {id}");
            }

            return null;
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            var key = PrefixConstants.GetRegistrationTicketKey(id);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write registration ticket {id}");
            }
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            var key = PrefixConstants.GetRegistrationTicketKey(ticket.Id);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            string stringValue = JsonSerializer.Serialize(ticket);
            byte[] bytesValue = Encoding.UTF8.GetBytes(stringValue);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write registration ticket {ticket.Id}");
            }
        }

        #endregion

        public List<string> GetMonitoringDatabases()
        {
            string listKey = PrefixConstants.GetMonitoringDatabasesListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(listKey);
            List<string> result = new List<string>();
            try
            {
                var products = _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                result.AddRange(products);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get databases list");
            }

            return result;
        }

        public void AddMonitoringDatabaseToList(string folderName)
        {
            var key = PrefixConstants.GetMonitoringDatabasesListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                var currentList = _database.TryRead(bytesKey, out var value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                if (!currentList.Contains(folderName))
                    currentList.Add(folderName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add monitoring database to list");
            }
        }

        public void RemoveMonitoringDatabaseFromList(string folderName)
        {
            var key = PrefixConstants.GetMonitoringDatabasesListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                var currentList = _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                currentList.Remove(folderName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        public void Dispose() => _database.Dispose();
    }
}
