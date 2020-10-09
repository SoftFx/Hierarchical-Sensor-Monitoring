using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LightningDB;
using HSMCommon.Extensions;
using HSMServer.Configuration;
using HSMServer.DataLayer.Model;
using HSMServer.Exceptions;
using HSMServer.Extensions;
using HSMServer.Model;
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
        private object _accessLock;
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

        private async Task AddSensorDataAsync(SensorDataObject dataObject)
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

                        var code = tx.Put(db, Encoding.ASCII.GetBytes(PrefixConstants.SERVERS_LIST_PREFIX),
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
                        var (code, key, value) = tx.Get(db, Encoding.ASCII.GetBytes(PrefixConstants.SERVERS_LIST_PREFIX));
                        tx.Commit();
                        result.AddRange(ParseServers(Encoding.ASCII.GetString(value.CopyToNewArray())));
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
                    _logger.Error($"GetSensorKey: failed to get key for {serverName}:{sensorName}");
                }
            });
            return result;
        }


        #endregion

        #region Sync code

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

                }
                catch (Exception e)
                {
                    //TODO: add logging
                    return false;
                }
            }

            return true;
        }
        public bool AddServer(string serverName)
        {
            lock (_accessLock)
            {
                try
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                    var code = tx.Put(db, Encoding.ASCII.GetBytes(PrefixConstants.SERVERS_LIST_PREFIX),
                        Encoding.ASCII.GetBytes($"{serverName};"), PutOptions.AppendData);
                    if (code != MDBResultCode.Success)
                    {
                        throw new Exception($"Failed to add server: code = {code}");
                    }
                }
                catch (Exception ex)
                {
                    //TODO: add logging                    
                    return false;
                }
            }

            return true;
        }

        public List<string> GetServersList()
        {
            List<string> result = new List<string>();
            lock (_accessLock)
            {
                try
                {
                    using var tx = environment.BeginTransaction();
                    using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });
                    var (code, key, value) = tx.Get(db, Encoding.ASCII.GetBytes(PrefixConstants.SERVERS_LIST_PREFIX));
                    result.AddRange(ParseServers(Encoding.ASCII.GetString(value.CopyToNewArray())));
                }
                catch (Exception ex)
                {
                    //TODO: add logging
                }
            }

            return result;
        }

        public string GetSensorKey(string serverName, string sensorName)
        {
            string result = string.Empty;

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
                _logger.Error($"GetSensorKey: failed to get key for {serverName}:{sensorName}");
            }

            return result;
        }

        #endregion


        private string GetSearchKey(string machineName, string sensorName)
        {
            return $"{PrefixConstants.JOB_SENSOR_PREFIX}_{machineName}_{sensorName}";
        }

        private string GetSensorInfoKey(SensorInfo info)
        {
            return $"{PrefixConstants.SENSOR_KEY_PREFIX}_{info.ServerName}_{info.SensorName}";
        }
        private long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long) timeSpan.TotalSeconds;
        }

        private IEnumerable<string> ParseServers(string serversListString)
        {
            List<string> result = new List<string>();
            string[] splitRes = serversListString.Split(";".ToCharArray());
            result.AddRange(splitRes.Select(srv => srv.Trim()));
            return result;
        }
        //private static string 
    }
}
