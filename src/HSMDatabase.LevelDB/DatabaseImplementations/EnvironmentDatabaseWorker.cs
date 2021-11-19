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
        private readonly IDatabase _database;
        private readonly Logger _logger;
        public EnvironmentDatabaseWorker(string name)
        {
            _database = new Database(name);
            _logger = LogManager.GetCurrentClassLogger();
        }

        #region Products

        public void AddProductToList(string productName)
        {
            var key = PrefixConstants.GetProductsListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                byte[] value = _database.Read(bytesKey);

                List<string> currentList = value == null
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value));
                if (!currentList.Contains(productName))
                    currentList.Add(productName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        public List<string> GetProductsList()
        {
            string listKey = PrefixConstants.GetProductsListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(listKey);
            List<string> result = new List<string>();
            try
            {
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException("Failed to read products list!");
                }

                List<string> products = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value));
                result.AddRange(products);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get products list");
            }

            return result;
        }

        public ProductEntity GetProductInfo(string productName)
        {
            string key = PrefixConstants.GetProductInfoKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException("Failed to read product info");
                }

                return JsonSerializer.Deserialize<ProductEntity>(Encoding.UTF8.GetString(value));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for product {productName}");
            }

            return null;
        }

        public void PutProductInfo(ProductEntity product)
        {
            string key = PrefixConstants.GetProductInfoKey(product.Name);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            string stringData = JsonSerializer.Serialize(product);
            byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put product info for {product.Name}");
            }
        }

        public void RemoveProductInfo(string productName)
        {
            string key = PrefixConstants.GetProductInfoKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove info for product {productName}");
            }
        }

        public void RemoveProductFromList(string productName)
        {
            var key = PrefixConstants.GetProductsListKey();
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                bool result = _database.TryRead(bytesKey, out byte[] value);
                if (!result)
                {
                    throw new ServerDatabaseException("Failed to read products list!");
                }

                string stringValue = Encoding.UTF8.GetString(value);
                List<string> currentList = string.IsNullOrEmpty(stringValue)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(stringValue);

                currentList.Remove(productName);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
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

        public List<string> GetSensorsList(string productName)
        {
            string listKey = PrefixConstants.GetSensorsListKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(listKey);
            List<string> result = new List<string>();
            try
            {
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException($"Failed to read sensors list for {productName}!");
                }

                string stringValue = Encoding.UTF8.GetString(value);
                List<string> products = string.IsNullOrEmpty(stringValue)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(stringValue);
                result.AddRange(products);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get products list for {productName}");
            }

            return result;
        }

        public void AddNewSensorToList(string productName, string path)
        {
            var key = PrefixConstants.GetSensorsListKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                byte[] value = _database.Read(bytesKey);

                List<string> currentList = value == null
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value));
                if (!currentList.Contains(path))
                    currentList.Add(path);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        public void RemoveSensorsList(string productName)
        {
            var key = PrefixConstants.GetSensorsListKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensors list for {productName}");
            }
        }

        public void RemoveSensorFromList(string productName, string path)
        {
            var key = PrefixConstants.GetSensorsListKey(productName);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                bool result = _database.TryRead(bytesKey, out byte[] value);
                if (!result)
                {
                    throw new ServerDatabaseException("Failed to read products list!");
                }

                string stringValue = Encoding.UTF8.GetString(value);
                List<string> currentList = string.IsNullOrEmpty(stringValue)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(stringValue);

                currentList.Remove(path);

                string stringData = JsonSerializer.Serialize(currentList);
                byte[] bytesValue = Encoding.UTF8.GetBytes(stringData);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add prodct to list");
            }
        }

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            string key = PrefixConstants.GetSensorInfoKey(productName, path);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException($"Failed to read sensor info for {productName}/{path}");
                }

                return JsonSerializer.Deserialize<SensorEntity>(Encoding.UTF8.GetString(value));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for sensor {productName}/{path}");
            }

            return null;
        }

        public void RemoveSensorValues(string productName, string path)
        {
            var key = PrefixConstants.GetSensorInfoKey(productName, path);
            byte[] bytesKey = Encoding.UTF8.GetBytes(key);
            try
            {
                _database.DeleteAllStartingWith(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor values for {productName}/{path}");
            }
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
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException("Failed to read configuration object info");
                }

                return JsonSerializer.Deserialize<ConfigurationEntity>(Encoding.UTF8.GetString(value));
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
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException("Failed to read ticket info");
                }

                return JsonSerializer.Deserialize<RegisterTicketEntity>(Encoding.UTF8.GetString(value));
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
                bool isRead = _database.TryRead(bytesKey, out byte[] value);
                if (!isRead)
                {
                    throw new ServerDatabaseException("Failed to read databases list!");
                }

                List<string> products = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value));
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
                byte[] value = _database.Read(bytesKey);

                List<string> currentList = value == null
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value));
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
                bool result = _database.TryRead(bytesKey, out byte[] value);
                if (!result)
                {
                    throw new ServerDatabaseException("Failed to read products list!");
                }

                string stringValue = Encoding.UTF8.GetString(value);
                List<string> currentList = string.IsNullOrEmpty(stringValue)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(stringValue);

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
    }
}
