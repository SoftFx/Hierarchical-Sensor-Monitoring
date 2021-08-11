using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Entity;
using HSMDatabase.LevelDB;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.EnvironmentDatabase
{
    internal class EnvironmentDatabaseWorker : IEnvironmentDatabase
    {
        private readonly IDatabase _database;
        private readonly Logger _logger;
        public EnvironmentDatabaseWorker(string name)
        {
            _database = new Database(name);
            _logger = LogManager.GetCurrentClassLogger(typeof(EnvironmentDatabaseWorker));
        }

        #region Products

        public void AddProductToList(string productName)
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

                List<string> currentList = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(bytesKey));
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

                List<string> currentList = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(bytesKey));
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

        public void RemoveSensor(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void AddSensor(SensorEntity info)
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

        public void RemoveSensorsList(string productName)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorFromList(string productName, string sensorName)
        {
            throw new NotImplementedException();
        }

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void RemoveSensorValues(string productName, string path)
        {
            throw new NotImplementedException();
        }

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


    }
}