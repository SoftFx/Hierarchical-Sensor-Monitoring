using HSMDatabase.Entity;
using HSMDatabase.Extensions;
using LevelDB;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class LevelDBDatabaseWorker : IDatabaseWorker
    {
        #region Singleton

        private static volatile LevelDBDatabaseWorker _instance;
        private static readonly object _singletonLockObj = new object();
        public static IDatabaseWorker GetInstance()
        {
            return Instance;
        }

        private static LevelDBDatabaseWorker Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLockObj)
                    {
                        if (_instance == null)
                        {
                            _instance = new LevelDBDatabaseWorker();
                        }
                    }
                }

                return _instance;
            }
        }
        #endregion

        #region IDisposable implementation

        private bool _disposed;

        // Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

            if (!_disposed)
            {
                if (disposingManagedResources)
                {
                    // Dispose managed resources here...
                    //foreach (var counter in Counters)
                    //  counter.Dispose();
                    lock (_accessLock)
                    {
                        _database?.Dispose();
                        _database = null;
                    }
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~LevelDBDatabaseWorker()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private readonly object _accessLock;
        private readonly ILogger _logger;
        private readonly char[] _keysSeparator = { '_' };
        private const string DATABASE_NAME = "MonitoringData";
        private readonly Options _dbOptions = new Options() { CreateIfMissing = true, MaxOpenFiles = 100000 };
        private DB _database;
        private string _currentDatabaseName = DATABASE_NAME;
        private LevelDBDatabaseWorker()
        {
            _accessLock = new object();
            _logger = LogManager.GetCurrentClassLogger();
            try
            {
                _database = new DB(_dbOptions, DATABASE_NAME, Encoding.UTF8);

            }
            catch (System.Exception e)
            {
                _logger.Error(e, "Failed to create LevelDB database");
                throw;
            }
            
        }


        #region Management

        public void CloseDatabase()
        {
            try
            {
                lock (_accessLock)
                {
                    _database.Close();
                    _database.Dispose();
                    _database = null;
                }
            }
            catch (Exception e)
            {
                _logger.Error("Failed to close the database", e);
            }
            
        }

        public void OpenDatabase(string databaseName)
        {
            try
            {
                lock (_accessLock)
                {
                    //Required Db is already opened
                    if (_database?.PropertyValue("name") == databaseName)
                        return;

                    if (_database != null)
                    {
                        _database.Close();
                        _database.Dispose();
                        _database = null;
                    }

                    _database = new DB(_dbOptions, databaseName, Encoding.UTF8);
                    _currentDatabaseName = databaseName;
                }
            }
            catch (Exception e)
            {
                _logger.Error("Failed to open new database", e);
            }
            
        }

        public void DeleteDatabase()
        {
            CloseDatabase();
            try
            {
                Directory.Delete(_currentDatabaseName, true);
            }
            catch (Exception e)
            {
                _logger.Error("Failed to delete database directory", e);
            }
        }

        #endregion

        #region ProductEntity

        public void AddProductToList(string productName)
        {
            try
            {
                lock (_accessLock)
                {
                    var currentValue = _database.Get(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    _database.Delete(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    var prodList = string.IsNullOrEmpty(currentValue)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(currentValue);
                    _logger.Info($"Products list read: {currentValue}");
                    prodList.Add(productName);
                    _database.Put(PrefixConstants.PRODUCTS_LIST_PREFIX, JsonSerializer.Serialize(prodList));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add product to list");
                throw;
            }
        }

        public List<string> GetProductsList()
        {
            List<string> result = new List<string>();
            try
            {
                lock (_accessLock)
                {
                    var value = _database.Get(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    result.AddRange(JsonSerializer.Deserialize<List<string>>(value));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get products list");
            }

            return result;
        }

        public ProductEntity GetProductInfo(string productName)
        {
            ProductEntity result = default(ProductEntity);
            try
            {
                lock (_accessLock)
                {
                    var value = _database.Get(GetProductInfoKey(productName));
                    result = JsonSerializer.Deserialize<ProductEntity>(value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get product info for product = {productName}");
            }

            return result;
        }

        public void PutProductInfo(ProductEntity product)
        {
            try
            {
                string key = GetProductInfoKey(product.Name);
                string value = JsonSerializer.Serialize(product);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add product info");
            }
        }

        public void RemoveProductInfo(string name)
        {
            try
            {
                string key = GetProductInfoKey(name);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove product info for product = {name}");
            }
        }

        public void RemoveProductFromList(string name)
        {
            try
            {
                lock (_accessLock)
                {
                    var currentValue = _database.Get(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    _database.Delete(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    List<string> list = JsonSerializer.Deserialize<List<string>>(currentValue);
                    list.Remove(name);
                    _database.Put(PrefixConstants.PRODUCTS_LIST_PREFIX, JsonSerializer.Serialize(list));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to remove product from list");
            }
        }

        #endregion

        #region Sensors

        public void RemoveSensor(string productName, string path)
        {
            try
            {
                string key = GetSensorInfoKey(productName, path);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor info for {path}");
            }
        }

        public void AddSensor(SensorEntity info)
        {
            try
            {
                string key = GetSensorInfoKey(info.ProductName, info.Path);
                string value = JsonSerializer.Serialize(info);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add sensor info for {info.Path}");
            }
        }
        public SensorEntity GetSensorInfo(string productName, string path)
        {
            SensorEntity sensorInfo = default(SensorEntity);
            try
            {
                string key = GetSensorInfoKey(productName, path);
                string value;
                lock (_accessLock)
                {
                    value = _database.Get(key);
                }

                sensorInfo = JsonSerializer.Deserialize<SensorEntity>(value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read SensorEntity for {productName}:{path}");
            }

            return sensorInfo;
        }
        public void RemoveSensorValues(string productName, string path)
        {
            try
            {
                string readKey = GetSensorReadValueKey(productName, path);
                byte[] bytesKey = Encoding.UTF8.GetBytes(readKey);
                int count = 0;
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        for (iterator.Seek(bytesKey); iterator.IsValid() && iterator.Key().StartsWith(bytesKey);
                            iterator.Next())
                        {
                            _database.Delete(iterator.Key());
                            ++count;
                        }    
                    }
                
                }
                _logger.Info($"Removed {count} values of sensor {path}");
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove values of sensors {path}");
            }
        }
        
        public void WriteSensorData(SensorDataEntity dataObject, string productName)
        {
            try
            {
                var key = GetSensorWriteValueKey(productName, dataObject.Path, dataObject.TimeCollected);
                string value = JsonSerializer.Serialize(dataObject);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add data for sensor {dataObject.Path}");
            }
        }

        public void WriteOneValueSensorData(SensorDataEntity dataObject, string productName)
        {
            try
            {
                var key = GetOneValueSensorWriteKey(productName, dataObject.Path);
                string value = JsonSerializer.Serialize(dataObject);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add data for sensor {dataObject.Path}");
            }
        }

        public SensorDataEntity GetOneValueSensorValue(string productName, string path)
        {
            try
            {
                string value = string.Empty;
                var key = Encoding.UTF8.GetBytes(GetSensorReadValueKey(productName, path));
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        iterator.Seek(key);
                        if (iterator.IsValid())
                        {
                            if (iterator.Key().StartsWith(key))
                            {
                                value = iterator.ValueAsString();
                            }
                        }
                    }
                }

                SensorDataEntity entity = JsonSerializer.Deserialize<SensorDataEntity>(value);
                return entity;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get one value sensor value for {productName}/{path}");
            }

            return null;
        }

        public SensorDataEntity GetLastSensorValue(string productName, string path)
        {
            SensorDataEntity sensorDataObject = default(SensorDataEntity);
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetSensorReadValueKey(productName, path));
                DateTime lastDateTime = DateTime.MinValue;
                byte[] bytesValue = new byte[0];
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        for (iterator.Seek(searchKey); iterator.IsValid() && iterator.Key().StartsWith(searchKey); iterator.Next())
                        {
                            try
                            {
                                DateTime currentDateTime = GetTimeFromSensorWriteKey(iterator.Key());
                                if (currentDateTime > lastDateTime)
                                {
                                    lastDateTime = currentDateTime;
                                    bytesValue = iterator.Value();
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Failed to read SensorDataEntity");
                            }
                        }
                    }
                }
                string stringValue = Encoding.UTF8.GetString(bytesValue);
                sensorDataObject = JsonSerializer.Deserialize<SensorDataEntity>(stringValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get last value for sensor = {path}, product = {productName}");
            }

            return sensorDataObject;
        }

        public List<SensorDataEntity> GetSensorDataHistory(string productName, string path, long n)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetSensorReadValueKey(productName, path));
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        for (iterator.SeekToFirst(); iterator.IsValid(); iterator.Next())
                        {
                            if (!iterator.Key().StartsWith(searchKey))
                                continue;

                            try
                            {
                                var typedValue = JsonSerializer.Deserialize<SensorDataEntity>(iterator.ValueAsString());
                                if (typedValue.Path == path)
                                {
                                    result.Add(typedValue);
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Failed to read SensorDataEntity");
                            }

                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get sensor history {path}");
            }

            return result;
        }

        public void RemoveSensorsList(string productName)
        {
            try
            {
                var key = GetSensorsListKey(productName);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensors list for {productName}");
            }
        }
        public List<string> GetSensorsList(string productName)
        {
            List<string> result = new List<string>();
            try
            {
                var key = GetSensorsListKey(productName);
                string value;
                lock (_accessLock)
                {
                    value = _database.Get(key);
                }
                result.AddRange(JsonSerializer.Deserialize<List<string>>(value));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get sensors list for {productName}");
            }

            return result;
        }

        public void AddNewSensorToList(string productName, string path)
        {
            try
            {
                string key = GetSensorsListKey(productName);
                lock (_accessLock)
                {
                    string currentValue = _database.Get(key);
                    _database.Delete(key);
                    var list = string.IsNullOrEmpty(currentValue)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(currentValue);
                    list.Add(path);
                    _database.Put(key, JsonSerializer.Serialize(list));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add new sensor {path} to list for product {productName}");
            }
        }

        public void RemoveSensorFromList(string productName, string sensorName)
        {
            try
            {
                string key = GetSensorsListKey(productName);
                lock (_accessLock)
                {
                    var currentValue = _database.Get(key);
                    _database.Delete(key);
                    List<string> list = JsonSerializer.Deserialize<List<string>>(currentValue);
                    list.Remove(sensorName);
                    _database.Put(key, JsonSerializer.Serialize(list));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor {sensorName} from list for {productName}");
            }
        }

        #endregion

        #region Configuration

        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            try
            {
                string key = GetUniqueConfigurationObjectKey(name);
                string value;
                lock (key)
                {
                    value = _database.Get(key);
                }

                return JsonSerializer.Deserialize<ConfigurationEntity>(value);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read ConfigurationEntity!");
                return null;
            }
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            try
            {
                string key = GetUniqueConfigurationObjectKey(obj.Name);
                string value = JsonSerializer.Serialize(obj);
                lock (_accessLock)
                {
                    _database.Delete(key);
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to save ConfigurationEntity!");
            }
        }

        public void RemoveConfigurationObject(string name)
        {
            try
            {
                string key = GetUniqueConfigurationObjectKey(name);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove configuration entity named {name}");
            }
        }
        #endregion

        #region Users

        public void AddUser(UserEntity user)
        {
            try
            {
                string key = GetUniqueUserKey(user.UserName);
                string value = JsonSerializer.Serialize(user);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write user data for user = '{user.UserName}'");
            }
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            var skip = (page - 1) * pageSize;
            int index = 1; 
            var lastIndex = page * pageSize;
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetUserReadKey());
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        for (iterator.Seek(searchKey); iterator.IsValid() && iterator.Key().StartsWith(searchKey) &&
                                                       index <= lastIndex; iterator.Next(), ++index)
                        {
                            if (index <= skip)
                                continue;

                            try
                            {
                                UserEntity user = JsonSerializer.Deserialize<UserEntity>(iterator.ValueAsString());
                                users.Add(user);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, $"Failed to deserialize user from {iterator.ValueAsString()}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read ");
            }

            return users;
        }
        public List<UserEntity> ReadUsers()
        {
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetUserReadKey());
                lock (_accessLock)
                {
                    using (var iterator = _database.CreateIterator())
                    {
                        for (iterator.Seek(searchKey); iterator.IsValid() && iterator.Key().StartsWith(searchKey); iterator.Next())
                        {
                            try
                            {
                                string stringValue = iterator.ValueAsString();
                                UserEntity user = JsonSerializer.Deserialize<UserEntity>(stringValue);
                                users.Add(user);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, $"Failed to deserialize user from {iterator.ValueAsString()}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read ");
            }

            return users;
        }

        public void RemoveUser(UserEntity user)
        {
            try
            {
                string key = GetUniqueUserKey(user.UserName);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove user = '{user.UserName}'");
            }
        }

        #endregion

        #region Registration Ticket

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            RegisterTicketEntity result = default(RegisterTicketEntity);
            try
            {
                lock (_accessLock)
                {
                    var value = _database.Get(GetRegistrationTicket(id));
                    result = JsonSerializer.Deserialize<RegisterTicketEntity>(value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get registration ticket = {id}");
            }

            return result;
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            try
            {
                string key = GetRegistrationTicket(id);
                lock (_accessLock)
                {
                    _database.Delete(key);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove registration ticket = '{id}'");
            }
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            try
            {
                string key = GetRegistrationTicket(ticket.Id);
                string value = JsonSerializer.Serialize(ticket);
                lock (_accessLock)
                {
                    _database.Put(key, value);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write registration ticket with id = '{ticket.Id}'");
            }
        }

        #endregion

        #region Private methods

        private string GetUniqueConfigurationObjectKey(string name)
        {
            return $"{PrefixConstants.CONFIGURATION_OBJECT_PREFIX}_{name}";
        }
        private string GetUniqueUserKey(string userName)
        {
            return $"{PrefixConstants.USER_INFO_PREFIX}_{userName}";
        }

        private string GetUserReadKey()
        {
            return PrefixConstants.USER_INFO_PREFIX;
        }
        private string GetSensorsListKey(string productName)
        {
            return $"{PrefixConstants.SENSORS_LIST_PREFIX}_{productName}";
        }
        private string GetSensorReadValueKey(string productName, string path)
        {
            return $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{path}";
        }

        private string GetOneValueSensorWriteKey(string productName, string path)
        {
            return $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{path}_{DateTime.Now:G}_{DateTime.Now.Ticks}";
        }
        private string GetSensorWriteValueKey(string productName, string path, DateTime putTime)
        {
            return
                $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{path}_{putTime:G}_{putTime.Ticks}";
        }
        
        private string GetSensorInfoKey(string productName, string path)
        {
            return $"{PrefixConstants.SENSOR_KEY_PREFIX}_{productName}_{path}";
        }
        private string GetProductInfoKey(string name)
        {
            return $"{PrefixConstants.PRODUCT_INFO_PREFIX}_{name}";
        }

        private string GetRegistrationTicket(Guid id)
        {
            return $"{PrefixConstants.REGISTRATION_TICKET_PREFIX}_{id}";
        }

        private DateTime GetTimeFromSensorWriteKey(byte[] keyBytes)
        {
            string str = Encoding.UTF8.GetString(keyBytes);
            var splitRes = str.Split(_keysSeparator);
            str = splitRes[^2];
            try
            {
                return DateTime.Parse(str);
            }
            catch (Exception e)
            {
                //_logger.Error(e, $"Error parsing datetime: {str}");
            }
            //Back compatibility
            str = splitRes.Last();
            try
            {
                return DateTime.Parse(str);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Error parsing datetime from prev version: {str}");
                return DateTime.MinValue;
            }
        }
        private long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        #endregion
    }
}
