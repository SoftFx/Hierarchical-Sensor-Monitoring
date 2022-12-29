using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal class EnvironmentDatabaseWorker : IEnvironmentDatabase
    {
        private readonly LevelDBDatabaseAdapter _database;
        private readonly Logger _logger;

        private readonly byte[] _accessKeyListKey = Encoding.UTF8.GetBytes(PrefixConstants.GetAccessKeyListKey());
        private readonly byte[] _productListKey = Encoding.UTF8.GetBytes(PrefixConstants.GetProductsListKey());
        private readonly byte[] _sensorIdsKey = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorIdsKey());
        private readonly byte[] _policyIdsKey = Encoding.UTF8.GetBytes(PrefixConstants.GetPolicyIdsKey());


        public EnvironmentDatabaseWorker(string name)
        {
            _database = new LevelDBDatabaseAdapter(name);
            _logger = LogManager.GetCurrentClassLogger();
        }


        #region Products

        public void AddProductToList(string productId)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out var value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                if (!currentList.Contains(productId))
                    currentList.Add(productId);

                _database.Put(_productListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
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

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(product));
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

        public void RemoveProductFromList(string productId)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out byte[] value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                currentList.Remove(productId);

                _database.Put(_productListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove prodct {productId} from list");
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

                _database.Put(_accessKeyListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
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

                _database.Put(_accessKeyListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove AccessKey {id} from list");
            }
        }

        public void AddAccessKey(AccessKeyEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(entity));
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

        public void AddSensorIdToList(string sensorId)
        {
            void AddSensorIdToListIfNotExist(List<string> sensorIds)
            {
                if (!sensorIds.Contains(sensorId))
                    sensorIds.Add(sensorId);
            }

            UpdateSensorIdsList(AddSensorIdToListIfNotExist, $"Failed to add sensor id {sensorId} to list");
        }

        public void AddSensor(SensorEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);
            var bytesValue = JsonSerializer.SerializeToUtf8Bytes(entity);

            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add sensor info for {entity.Id}");
            }
        }

        public void RemoveSensorIdFromList(string sensorId) =>
            UpdateSensorIdsList(sensorIdsList => sensorIdsList.Remove(sensorId),
                                $"Failed to remove sensor id {sensorId} from list");

        public void RemoveSensor(string sensorId)
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(sensorId);

            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor info for {sensorId}");
            }
        }

        public SensorEntity GetSensorEntity(string sensorId)
        {
            var bytesKey = Encoding.UTF8.GetBytes(sensorId);

            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<SensorEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for sensor {sensorId}");
            }

            return null;
        }

        public List<string> GetAllSensorsIds() =>
            GetListOfKeys(_sensorIdsKey, "Failed to get sensors ids list");

        private void UpdateSensorIdsList(Action<List<string>> updateListAction, string errorMessage)
        {
            try
            {
                var sensorIds = GetAllSensorsIds();

                updateListAction?.Invoke(sensorIds);

                _database.Put(_sensorIdsKey, JsonSerializer.SerializeToUtf8Bytes(sensorIds));
            }
            catch (Exception e)
            {
                _logger.Error(e, errorMessage);
            }
        }

        #endregion

        #region Policies

        public void AddPolicyIdToList(string policyId)
        {
            try
            {
                var policyIds = GetAllPoliciesIds();

                if (!policyIds.Contains(policyId))
                    policyIds.Add(policyId);

                _database.Put(_policyIdsKey, JsonSerializer.SerializeToUtf8Bytes(policyIds));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add policy id {policyId} to list");
            }
        }

        public void RemovePolicyFromList(string id)
        {
            try
            {
                var policyIds = GetAllPoliciesIds();
                policyIds.Remove(id);

                _database.Put(_policyIdsKey, JsonSerializer.SerializeToUtf8Bytes(policyIds));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove Policy {id} from list");
            }
        }

        public void AddPolicy(PolicyEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);
            var bytesValue = JsonSerializer.SerializeToUtf8Bytes(entity.Policy);

            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add policy info for {entity.Id}");
            }
        }

        public void RemovePolicy(string id)
        {
            var bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove Policy by {id}");
            }
        }

        public List<string> GetAllPoliciesIds() =>
            GetListOfKeys(_policyIdsKey, "Failed to get all policy ids");

        public byte[] GetPolicy(string policyId)
        {
            var bytesKey = Encoding.UTF8.GetBytes(policyId);

            try
            {
                return _database.TryRead(bytesKey, out byte[] value) ? value : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for sensor {policyId}");
            }

            return null;
        }

        #endregion 

        #region User

        public void AddUser(UserEntity user)
        {
            var userKey = PrefixConstants.GetUniqueUserKey(user.UserName);
            var keyBytes = Encoding.UTF8.GetBytes(userKey);

            try
            {
                _database.Put(keyBytes, JsonSerializer.SerializeToUtf8Bytes(user));
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

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(obj));
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

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(ticket));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write registration ticket {ticket.Id}");
            }
        }

        #endregion

        public void Dispose() => _database.Dispose();

        private List<string> GetListOfKeys(byte[] key, string error)
        {
            try
            {
                return _database.TryRead(key, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();
            }
            catch (Exception e)
            {
                _logger.Error(e, error);
            }

            return new();
        }
    }
}
