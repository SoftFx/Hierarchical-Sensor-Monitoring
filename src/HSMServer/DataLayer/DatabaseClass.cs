using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Configuration;
using LightningDB;
using HSMServer.DataLayer.Model;
using HSMServer.Exceptions;
using HSMServer.Extensions;
using NLog;

namespace HSMServer.DataLayer
{
    [Obsolete("02.04.2021 Use LevelDB database.")]
    internal class DatabaseClass : IDisposable, IDatabaseClass
    {
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
                    environment.Dispose();
                    environment = null;
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~DatabaseClass()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private const string ENVIRONMENT_PATH = @"Database";
        private const string DATABASE_NAME = "monitoring";
        private static LightningEnvironment environment;
        private readonly object _accessLock;
        private readonly Logger _logger;
        private readonly char[] _keysSeparator = {'_'};

        public DatabaseClass()
        {
            _logger = LogManager.GetCurrentClassLogger();
            try
            {
                environment = new LightningEnvironment(ENVIRONMENT_PATH);
                //Might need to increase later. Current size is 1.25 GB
                environment.MapSize = 1342177280;
                environment.MaxDatabases = 1;
                //environment.MaxReaders = Config.UsersCount;
                environment.MaxReaders = 10;
                environment.Open();
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "Failed to open database environment!");
            }
            
            _accessLock = new object();
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create });
                    var code = tx.Commit();
                    db.Dispose();
                    tx.Dispose();
                }
                _logger.Info("DatabaseClass initialized, monitoring database exists");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "DatabaseClass: failed to create/check database existance in constructor!");
                throw;
            }
        }

        #region Async code

        //private async Task AddSensorDataAsync(SensorDataObject dataObject, string productName, string path)
        //{
        //    await Task.Run(() =>
        //    {
        //        try
        //        {
        //            lock (_accessLock)
        //            {
        //                using var tx = environment.BeginTransaction();
        //                using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
        //                dataObject.TimeCollected = DateTime.Now;

        //                string json = JsonSerializer.Serialize(dataObject);

        //                var keyString = GetSensorWriteSearchKey(productName, path, dataObject.TimeCollected);
        //                var code = tx.Put(db, Encoding.UTF8.GetBytes(keyString), Encoding.UTF8.GetBytes(json));
        //                tx.Commit();

        //                if (code != MDBResultCode.BoolValue)
        //                {
        //                    throw new ServerDatabaseException($"Failed to put data, code = {code}");
        //                }
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.Error(ex, $"Failed to add object {dataObject.ToShortString()}");
        //        }
        //    });
        //}
        public async Task<bool> AddSensorAsync(SensorInfo info)
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_accessLock)
                    {
                        using var tx = environment.BeginTransaction();
                        using var db = tx.OpenDatabase(DATABASE_NAME,
                            new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                        string stringVal = JsonSerializer.Serialize(info);
                        var code = tx.Put(db, Encoding.UTF8.GetBytes(GetSensorInfoKey(info)),
                            Encoding.UTF8.GetBytes(stringVal));
                        tx.Commit();
                    }
                });
            }
            catch (Exception e)
            {
                //TODO: add logging
                return false;
            }
            return true;
        }
        public async Task<bool> AddServerAsync(string serverName)
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_accessLock)
                    {
                        using var tx = environment.BeginTransaction();
                        using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                        var code = tx.Put(db, Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX),
                            Encoding.UTF8.GetBytes($"{serverName};"), PutOptions.AppendData);
                        tx.Commit();
                        if (code != MDBResultCode.Success)
                        {
                            throw new Exception($"Failed to add server: code = {code}");
                        }
                    }
                });

            }
            catch (Exception ex)
            {
                //TODO: add logging                    
                return false;
            }

            return true;
        }

        public async Task<List<string>> GetServersListAsync()
        {
            List<string> result = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    lock (_accessLock)
                    {
                        using var tx = environment.BeginTransaction();
                        using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                        var (code, key, value) = tx.Get(db, Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX));
                        tx.Commit();
                        result.AddRange(ParseProducts(Encoding.UTF8.GetString(value.CopyToNewArray())));
                    }
                });
            }
            catch (Exception ex)
            {
                //TODO: add logging
            }

            return result;
        }

        public async Task<string> GetSensorKeyAsync(string serverName, string sensorName)
        {
            string result = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    lock (_accessLock)
                    {
                        using var tx = environment.BeginTransaction();
                        using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                        var (code, key, value) = tx.Get(db, Encoding.UTF8.GetBytes($"{PrefixConstants.SENSOR_KEY_PREFIX}_{serverName}_{sensorName}"));
                        tx.Commit();
                        result = Encoding.UTF8.GetString(value.CopyToNewArray());
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e,$"GetSensorKey: failed to get key for {serverName}:{sensorName}");
                }
            });
            return result;
        }


        #endregion

        #region Sync code

        #region Products' methods
        public void AddProductToList(string productName)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var bytesKey = Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    var (prevCode, prevKey, prevValue) = tx.Get(db, bytesKey);
                    if (prevCode != MDBResultCode.Success && prevCode != MDBResultCode.NotFound)
                    {
                        throw new ServerDatabaseException("Failed to read products list", prevCode);
                    }

                    var delCode = tx.Delete(db, bytesKey);
                    if (delCode != MDBResultCode.Success && delCode != MDBResultCode.NotFound)
                    {
                        throw new ServerDatabaseException("Failed to delete products list", delCode);
                    }
                    string stringVal = Encoding.UTF8.GetString(prevValue.CopyToNewArray());
                    var prodList = string.IsNullOrEmpty(stringVal) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(stringVal);
                    _logger.Info($"Products list read: {stringVal}");
                    prodList.Add(productName);
                    var code = tx.Put(db, bytesKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(prodList)));
                    var commitCode = tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to add product", code);
                    }

                    if (commitCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException($"Incorrect commit code", commitCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to add product to list");
            }
        }

        public List<string> GetProductsList()
        {
            List<string> result = new List<string>();
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    var (code, key, value) = tx.Get(db, Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX));
                    tx.Commit();
                    result.AddRange(JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value.CopyToNewArray())));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get products list!");
            }
            
            return result;
        }

        public Product GetProductInfo(string productName)
        {
            Product result = null;
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var (code, key, value) = tx.Get(db,
                        Encoding.UTF8.GetBytes($"{PrefixConstants.PRODUCT_INFO_PREFIX}_{productName}"));
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    result =
                        JsonSerializer.Deserialize<Product>(Encoding.UTF8.GetString(value.CopyToNewArray()));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get product info, product = {productName}");
            }

            return result;
        }

        public void PutProductInfo(Product product)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var code = tx.Put(db,
                        Encoding.UTF8.GetBytes(GetProductInfoKey(product.Name)),
                        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(product)));
                    var commitCode = tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    if (commitCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Commit code is wrong", commitCode);
                    }
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
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var bytesKey = Encoding.UTF8.GetBytes(GetProductInfoKey(name));
                    var code = tx.Delete(db, bytesKey);
                    tx.Commit();

                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }
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
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var bytesKey = Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    var (prevCode, prevKey, prevValue) = tx.Get(db, bytesKey);
                    if (prevCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to read products list", prevCode);
                    }
                    
                    var delCode = tx.Delete(db, bytesKey);
                    if (delCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to delete products list", delCode);
                    }
                    List<string> typedList =
                        JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(prevValue.CopyToNewArray()));
                    _logger.Info($"Read {typedList.Count} products from list");
                    typedList.Remove(name);

                    var code = tx.Put(db, bytesKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(typedList)));
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to remove product", code);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to remove product");
            }
        }
        #endregion

        #region Sensors' methods

        public void RemoveSensor(SensorInfo info)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    byte[] keyValue = Encoding.UTF8.GetBytes(GetSensorInfoKey(info));
                    var code = tx.Delete(db, keyValue);
                    tx.Commit();

                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Removing sensor = {info.SensorName}, product = {info.ProductName} error");
            }
        }
        public void AddSensor(SensorInfo info)
        {
            try
            {
                string stringVal = JsonSerializer.Serialize(info);
                string key = GetSensorInfoKey(info);
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var code = tx.Put(db, Encoding.UTF8.GetBytes(key),
                        Encoding.UTF8.GetBytes(stringVal));
                    var commitCode = tx.Commit();

                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    if (commitCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Commit code is not success", commitCode);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Adding sensor sensor = {info.SensorName}, product = {info.ProductName} error");
            }
            
        }
        public void WriteSensorData(SensorDataObject dataObject, string productName)
        {
            try
            {
                var keyString = GetSensorWriteValueKey(productName, dataObject.Path, dataObject.TimeCollected);
                //_logger.Info($"Adding record with key = {keyString}");
                string json = JsonSerializer.Serialize(dataObject);
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var code = tx.Put(db, Encoding.UTF8.GetBytes(keyString), Encoding.UTF8.GetBytes(json));
                    var commitCode = tx.Commit();

                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException($"Failed to put data, code = {code}");
                    }

                    if (commitCode != MDBResultCode.Success)
                    {
                        //
                        throw new ServerDatabaseException($"Failed to commit put transaction, code = {code}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to add object for {productName}/{dataObject.Path}");
            }
        }

        public void WriteOneValueSensorData(SensorDataObject dataObject, string productName)
        {
            throw new NotImplementedException();
        }

        public void WriteUniqueSensorData(SensorDataObject dataObject, string productName)
        {
            throw new NotImplementedException();
        }

        public SensorDataObject GetLastSensorValue(string productName, string path)
        {
            SensorDataObject sensorDataObject = null;
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetSensorReadValueKey(productName, path));
                DateTime lastDateTime = DateTime.MinValue;
                byte[] bytesValue = new byte[0];
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    using var cursor = tx.CreateCursor(db);
                    
                    var rangeCode = cursor.SetRange(searchKey);

                    if (rangeCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Set range failed", rangeCode);
                    }
                    
                    do
                    {
                        var (code, key, value) = cursor.GetCurrent();
                        if (key.CopyToNewArray().StartsWith(searchKey))
                        {
                            DateTime currentDateTime = GetTimeFromSensorWriteKey(key.CopyToNewArray());
                            if (currentDateTime > lastDateTime)
                            {
                                lastDateTime = currentDateTime;
                                bytesValue = value.CopyToNewArray();
                            }
                            //byte[] bytesValue = value.CopyToNewArray();
                            //break;
                        }
                        else
                        {
                            break;
                        }

                    } while (cursor.Next() == MDBResultCode.Success);
                    cursor.Dispose();
                    tx.Commit();
                }
                string stringValue = Encoding.UTF8.GetString(bytesValue);
                sensorDataObject = JsonSerializer.Deserialize<SensorDataObject>(stringValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get last value for sensor = {path}, product = {productName}");
            }

            return sensorDataObject;
        }

        public List<SensorDataObject> GetSensorDataHistory(string productName, string path, long n)
        {
            List<SensorDataObject> result = new List<SensorDataObject>();
            try
            {
                byte[] searchKey = Encoding.UTF8.GetBytes(GetSensorReadValueKey(productName, path));
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    using var cursor = tx.CreateCursor(db);
                    
                    var rangeCode = cursor.First();

                    if (rangeCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Set range failed", rangeCode);
                    }

                    var count = tx.GetEntriesCount(db);
                    //long count = 0;
                    do
                    {
                        var (code, key, value) = cursor.GetCurrent();
                        if (key.CopyToNewArray().StartsWith(searchKey))
                        {
                            try
                            {
                                var typedValue = GetTypedValue<SensorDataObject>(value);
                                if (typedValue.Path == path)
                                {
                                    result.Add(typedValue);
                                }
                                //++count;
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Failed to read sensorDataObject");
                            }

                            //if (n != -1 && count == n)
                            //{
                            //    break;
                            //}
                        }
                        //else
                        //{
                        //    break;
                        //}

                    } while (cursor.Next() == MDBResultCode.Success);
                    cursor.Dispose();
                    tx.Commit();
                }

            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get history for sensor = {path} in product = {productName}");
            }
            return result;
        }
        
        public List<string> GetSensorsList(string productName)
        {
            List<string> result = new List<string>();
            try
            {
                byte[] bytesKey = Encoding.UTF8.GetBytes(GetSensorsListKey(productName));
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    
                    var (code, key, value) = tx.Get(db, bytesKey);
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    result = JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value.CopyToNewArray()));
                }
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
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    byte[] bytesKey = Encoding.UTF8.GetBytes(GetSensorsListKey(productName));
                    var (getCode, getKey, getValue) = tx.Get(db, bytesKey);
                    if (getCode != MDBResultCode.Success && getCode != MDBResultCode.NotFound)
                    {
                        throw new ServerDatabaseException("Failed to get sensors list", getCode);
                    }

                    var delCode = tx.Delete(db, bytesKey);
                    if (delCode != MDBResultCode.Success && delCode != MDBResultCode.NotFound)
                    {
                        throw new ServerDatabaseException("Failed to delete sensors list", delCode);
                    }

                    var stringValue = Encoding.UTF8.GetString(getValue.CopyToNewArray());
                    List<string> typedList = string.IsNullOrEmpty(stringValue) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(stringValue);
                    _logger.Info($"Read {typedList.Count} sensors from list for {productName} product");
                    typedList.Add(path);

                    var serializedVal = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(typedList));
                    var code = tx.Put(db, bytesKey, serializedVal);
                    var commitCode = tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to write new list", code);
                    }

                    if (commitCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Incorrect commit code", commitCode);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add new sensor = {path} for product = {productName}");
            }
        }

        public void RemoveSensorFromList(string productName, string sensorName)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    byte[] bytesKey = Encoding.UTF8.GetBytes(GetSensorsListKey(productName));
                    var (getCode, getKey, getValue) = tx.Get(db, bytesKey);
                    if (getCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to get sensors list", getCode);
                    }

                    var delCode = tx.Delete(db, bytesKey);
                    if (delCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to delete sensors list", delCode);
                    }

                    var stringValue = Encoding.UTF8.GetString(getValue.CopyToNewArray());
                    List<string> typedList = string.IsNullOrEmpty(stringValue) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(stringValue);

                    typedList.Remove(sensorName);
                    var serializedVal = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(typedList));
                    var code = tx.Put(db, bytesKey, serializedVal);
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to write new list", code);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor = {sensorName} for product = {productName}");
            }
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public void AddUser(User user)
        {
            throw new NotImplementedException();
        }

        public List<User> ReadUsers()
        {
            throw new NotImplementedException();
        }

        public void RemoveUser(User user)
        {
            throw new NotImplementedException();
        }

        public List<User> ReadUsersPage(int page, int pageSize)
        {
            throw new NotImplementedException();
        }

        public ConfigurationObject ReadConfigurationObject()
        {
            throw new NotImplementedException();
        }

        public void WriteConfigurationObject(ConfigurationObject obj)
        {
            throw new NotImplementedException();
        }

        #endregion

        #endregion

        #region Sub-methods

        private string GetSensorsListKey(string productName)
        {
            return $"{PrefixConstants.SENSORS_LIST_PREFIX}_{productName}";
        }
        private string GetSensorReadValueKey(string productName, string path)
        {
            return $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{path}";
        }
        private string GetSensorWriteValueKey(string productName, string path, DateTime putTime)
        {
            return
                $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{path}_{putTime:G}_{putTime.Ticks}";
        }
        private string GetSensorInfoKey(SensorInfo info)
        {
            return $"{PrefixConstants.SENSOR_KEY_PREFIX}_{info.ProductName}_{info.Path}";
        }

        private string GetProductInfoKey(string name)
        {
            return $"{PrefixConstants.PRODUCT_INFO_PREFIX}_{name}";
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
                _logger.Error(e, $"Error parsing datetime: {str}");
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

        private IEnumerable<string> ParseProducts(string serversListString)
        {
            List<string> result = new List<string>();
            string[] splitRes = serversListString.Split(";".ToCharArray());
            result.AddRange(splitRes.Select(srv => srv.Trim()));
            return result;
        }

        private T GetTypedValue<T>(MDBValue value)
        {
            byte[] bytes = value.CopyToNewArray();
            string stringVal = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<T>(stringVal);
        }

        #endregion
        //private static string 

        #region Debug methods

        public void ClearProductsList()
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var bytesKey = Encoding.UTF8.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX);
                    var code = tx.Delete(db, bytesKey);
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }
                    _logger.Info("PRODUCTS LIST CLEARED!!!!");
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to clear products list!");
            }
        }

        #endregion
    }
}
