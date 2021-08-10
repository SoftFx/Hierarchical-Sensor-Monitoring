using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Entity;
using HSMDatabase.LevelDB;
using NLog;

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

        public void AddProductToList(string productName)
        {
            throw new NotImplementedException();
        }

        public List<string> GetProductsList()
        {
            throw new NotImplementedException();
        }

        public ProductEntity GetProductInfo(string productName)
        {
            throw new NotImplementedException();
        }

        public void PutProductInfo(ProductEntity product)
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


        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            throw new NotImplementedException();
        }

        public void RemoveConfigurationObject(string name)
        {
            throw new NotImplementedException();
        }

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            throw new NotImplementedException();
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            throw new NotImplementedException();
        }
    }
}