using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using LightningDB;
using HSMServer.DataLayer.Model;
using HSMServer.Exceptions;
using HSMServer.Extensions;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.DataLayer
{
    public class DatabaseClass : IDisposable
    {
        #region Singleton

        private static volatile DatabaseClass _instance;
        private static readonly object _syncRoot = new object();

        //Multithread singleton
        public static DatabaseClass Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                            _instance = new DatabaseClass();
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

        public DatabaseClass()
        {
            _logger = LogManager.GetCurrentClassLogger();
            environment = new LightningEnvironment(ENVIRONMENT_PATH);
            environment.MaxDatabases = 1;
            //environment.MaxReaders = Config.UsersCount;
            environment.MaxReaders = 10;
            environment.Open();
            _accessLock = new object();
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create });
                    tx.Commit();
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

        private async Task AddSensorDataAsync(SensorDataObject dataObject, string productName, string sensorName)
        {
            await Task.Run(() =>
            {
                try
                {
                    lock (_accessLock)
                    {
                        using var tx = environment.BeginTransaction();
                        using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                        dataObject.TimeCollected = DateTime.Now;

                        string json = JsonSerializer.Serialize(dataObject);

                        var keyString = GetSensorWriteSearchKey(productName, sensorName, dataObject.TimeCollected);
                        var code = tx.Put(db, Encoding.ASCII.GetBytes(keyString), Encoding.ASCII.GetBytes(json));
                        tx.Commit();

                        if (code != MDBResultCode.Success)
                        {
                            throw new ServerDatabaseException($"Failed to put data, code = {code}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to add object {dataObject.ToShortString()}");
                }
            });
        }
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
                        var code = tx.Put(db, Encoding.ASCII.GetBytes(GetSensorInfoKey(info)),
                            Encoding.ASCII.GetBytes(stringVal));
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

                        var code = tx.Put(db, Encoding.ASCII.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX),
                            Encoding.ASCII.GetBytes($"{serverName};"), PutOptions.AppendData);
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
                        var (code, key, value) = tx.Get(db, Encoding.ASCII.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX));
                        tx.Commit();
                        result.AddRange(ParseProducts(Encoding.ASCII.GetString(value.CopyToNewArray())));
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
                        var (code, key, value) = tx.Get(db, Encoding.ASCII.GetBytes($"{PrefixConstants.SENSOR_KEY_PREFIX}_{serverName}_{sensorName}"));
                        tx.Commit();
                        result = Encoding.ASCII.GetString(value.CopyToNewArray());
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

                    var bytesKey = Encoding.ASCII.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX);
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
                    string stringVal = Encoding.ASCII.GetString(prevValue.CopyToNewArray());
                    _logger.Info($"Products list read: {stringVal}");
                    stringVal += $"{productName};";
                    var code = tx.Put(db, bytesKey, Encoding.ASCII.GetBytes(stringVal));
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to add product", code);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to add product");
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
                    var (code, key, value) = tx.Get(db, Encoding.ASCII.GetBytes(PrefixConstants.PRODUCTS_LIST_PREFIX));
                    result.AddRange(ParseProducts(Encoding.ASCII.GetString(value.CopyToNewArray())));
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
                        Encoding.ASCII.GetBytes($"{PrefixConstants.PRODUCT_INFO_PREFIX}_{productName}"));
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    result =
                        JsonSerializer.Deserialize<Product>(Encoding.ASCII.GetString(value.CopyToNewArray()));
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
                        Encoding.ASCII.GetBytes($"{PrefixConstants.PRODUCT_INFO_PREFIX}_{product.Name}"),
                        Encoding.ASCII.GetBytes(JsonSerializer.Serialize(product)));
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add product info");
            }
        }
        #endregion

        #region Sensors' methods

        public bool AddSensor(SensorInfo info)
        {
            lock (_accessLock)
            {
                try
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    string stringVal = JsonSerializer.Serialize(info);
                    var code = tx.Put(db, Encoding.ASCII.GetBytes(GetSensorInfoKey(info)),
                        Encoding.ASCII.GetBytes(stringVal));
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException();
                    }

                }
                catch (Exception e)
                {
                    _logger.Error(e, $"Adding sensor sensor = {info.SensorName}, server = {info.ProductName} error");
                    return false;
                }
            }

            return true;
        }
        public void WriteSensorData(SensorDataObject dataObject, string productName, string sensorName)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    dataObject.TimeCollected = DateTime.Now;

                    string json = JsonSerializer.Serialize(dataObject);

                    var keyString = GetSensorWriteValueKey(productName, sensorName, dataObject.TimeCollected);
                    var code = tx.Put(db, Encoding.ASCII.GetBytes(keyString), Encoding.ASCII.GetBytes(json));
                    tx.Commit();

                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException($"Failed to put data, code = {code}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to add object {dataObject.ToShortString()}");
            }
        }

        public SensorDataObject GetLastSensorValue(string productName, string sensorName)
        {
            SensorDataObject sensorDataObject = null;
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    using var cursor = tx.CreateCursor(db);
                    var lastCode = cursor.Last();
                    if (lastCode != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Get last value failed", lastCode);
                    }

                    byte[] searchKey = Encoding.ASCII.GetBytes(GetSensorReadValueKey(productName, sensorName));
                    do
                    {
                        var (code, key, value) = cursor.GetCurrent();
                        if (key.CopyToNewArray().StartsWith(searchKey))
                        {
                            byte[] bytesValue = value.CopyToNewArray();
                            string stringValue = Encoding.ASCII.GetString(bytesValue);
                            sensorDataObject = JsonSerializer.Deserialize<SensorDataObject>(stringValue);
                            break;
                        }

                    } while (cursor.Previous() == MDBResultCode.Success);
                    cursor.Dispose();
                    tx.Commit();
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get last value for sensor = {sensorName}, product = {productName}");
            }

            return sensorDataObject;
        }

        public List<string> GetSensorsList(string productName)
        {
            List<string> result = null;
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    byte[] bytesKey = Encoding.ASCII.GetBytes(GetSensorsListKey(productName));
                    var (code, key, value) = tx.Get(db, bytesKey);
                    tx.Commit();
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException(code);
                    }

                    result = JsonSerializer.Deserialize<List<string>>(Encoding.ASCII.GetString(value.CopyToNewArray()));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get sensors list for {productName}");
            }

            return result;
        }

        public void AddNewSensor(string productName, string sensorName)
        {
            try
            {
                lock (_accessLock)
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    byte[] bytesKey = Encoding.ASCII.GetBytes(GetSensorsListKey(productName));
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

                    List<string> typedList =
                        JsonSerializer.Deserialize<List<string>>(Encoding.ASCII.GetString(getValue.CopyToNewArray()));
                    typedList.Add(sensorName);
                    var serializedVal = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(typedList));
                    var code = tx.Put(db, bytesKey, serializedVal);
                    if (code != MDBResultCode.Success)
                    {
                        throw new ServerDatabaseException("Failed to write new list", code);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add new sensor = {sensorName} for product = {productName}");
            }
        }
        #endregion

        #endregion

        #region Sub-methods

        private string GetSensorsListKey(string productName)
        {
            return $"{PrefixConstants.SENSORS_LIST_PREFIX}_{productName}";
        }
        private string GetSensorReadValueKey(string productName, string sensorName)
        {
            return $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{sensorName}";
        }
        private string GetSensorWriteValueKey(string productName, string sensorName, DateTime putTime)
        {
            return
                $"{PrefixConstants.SENSOR_VALUE_PREFIX}_{productName}_{sensorName}_{putTime.ToString()}";
        }
        private string GetSensorInfoKey(SensorInfo info)
        {
            return $"{PrefixConstants.SENSOR_KEY_PREFIX}_{info.ProductName}_{info.SensorName}";
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

        #endregion
        //private static string 
    }
}
