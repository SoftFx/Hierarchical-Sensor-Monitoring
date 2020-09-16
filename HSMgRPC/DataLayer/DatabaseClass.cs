using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMServer.Model;
using LightningDB;
using MAMSServer.Configuration;
using Microsoft.EntityFrameworkCore.Internal;
using HSMCommon.DataObjects;

namespace HSMgRPC.DataLayer
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

        private DatabaseClass()
        {
            environment = new LightningEnvironment(ENVIRONMENT_PATH);
            environment.MaxDatabases = 1;
            //environment.MaxReaders = Config.UsersCount;
            environment.MaxReaders = 10;
            environment.Open();
            using var tx = environment.BeginTransaction();
            using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create });
            tx.Commit();
            db.Dispose();
            tx.Dispose();
        }

        public async Task<List<ShortSensorData>> GetSensorsDataAsync(string machineName, string sensorName, int n)
        {
            string keyString = GenerateSearchKey(machineName, sensorName);
            if (string.IsNullOrEmpty(keyString))
            {
                return null;
            }

            return await ReadSensorsDataAsync(keyString, n);
        }

        public async Task<string> GetSensorDataAsync(string machineName, string sensorName)
        {
            string keyString = GenerateSearchKey(machineName, sensorName);
            if (string.IsNullOrEmpty(keyString))
            {
                return null;
            }

            return await ReadSingleSensorDataAsync(keyString);
        }
        /// <summary>
        /// Returns n last records, pass n less or equal 0 to get all records, corresponding to the given key
        /// </summary>
        /// <param name="keyString"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        private  async Task<List<ShortSensorData>> ReadSensorsDataAsync(string keyString, int n)
        {
            List<ShortSensorData> result = new List<ShortSensorData>();
            byte[] bytesKey = Encoding.ASCII.GetBytes(keyString);

            await Task.Run(() =>
            {
                using var tx = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
                using var db = tx.OpenDatabase(DATABASE_NAME);

                int count = 0;

                using var cursor = tx.CreateCursor(db);
                var resultCode = cursor.Last();
                if (resultCode == MDBResultCode.Success)
                {
                    do
                    {
                        var (code, key, value) = cursor.GetCurrent();
                        if (key.CopyToNewArray().StartsWith(bytesKey))
                        {
                            byte[] bytesValue = value.CopyToNewArray();
                            string stringValue = Encoding.ASCII.GetString(bytesValue);
                            try
                            {
                                ShortSensorData shortData = JsonSerializer.Deserialize<ShortSensorData>(stringValue);
                                result.Add(shortData);
                                ++count;
                                if (count == n)
                                {
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    } while (cursor.Previous() == MDBResultCode.Success);
                }
                cursor.Dispose();
            });

            return result;
        }
        /// <summary>
        /// Returns last record, corresponding to the given key
        /// </summary>
        /// <param name="keyString"></param>
        /// <returns></returns>
        private async Task<string> ReadSingleSensorDataAsync(string keyString)
        {
            string resultStr = string.Empty;
            byte[] bytesKey = Encoding.ASCII.GetBytes(keyString);
            
            await Task.Run(() =>
            {
                using var tx = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
                using var db = tx.OpenDatabase(DATABASE_NAME);
                using var cursor = tx.CreateCursor(db);
                var resultCode = cursor.Last();
                if (resultCode == MDBResultCode.Success)
                {
                    do
                    {
                        var (code, key, value) = cursor.GetCurrent();
                        if (key.CopyToNewArray().StartsWith(bytesKey))
                        {
                            byte[] bytesValue = value.CopyToNewArray();
                            string stringValue = Encoding.ASCII.GetString(bytesValue);
                            try
                            {
                                ShortSensorData shortData = JsonSerializer.Deserialize<ShortSensorData>(stringValue);
                                resultStr = stringValue;
                                break;
                            }
                            catch (Exception ex)
                            {

                            }
                        }

                    } while (cursor.Previous() == MDBResultCode.Success);
                }
            });

            return resultStr;
        }

        private  string GenerateSearchKey(string machineName, string sensorName)
        {
            return $"{machineName}_{sensorName}";
        }

        public async Task<bool> PutSingleSensorDataAsync(SensorData sensorData)
        {
            if (!Config.IsKeyRegistered(sensorData.Key))
            {
                return false;
            }

            ShortSensorData resultObject = new ShortSensorData { Comment = sensorData.Comment, Success = sensorData.Success, Time = sensorData.Time };
            string keyString = Config.GenerateDataStorageKey(sensorData.Key);
            return await PutSensorDataAsync(resultObject, keyString);
        }

        private async Task<bool> PutSensorDataAsync(ShortSensorData sensorData, string keyString)
        {
            try
            {
                _ = Task.Run(() =>
                  {
                      using var tx = environment.BeginTransaction();
                      using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration { Flags = DatabaseOpenFlags.None });

                      string json = JsonSerializer.Serialize(sensorData);

                      using (var cursor = tx.CreateCursor(db))
                      {
                          cursor.Put(Encoding.ASCII.GetBytes(keyString), Encoding.ASCII.GetBytes(json), CursorPutOptions.NoOverwrite);
                      }

                      var count = tx.GetEntriesCount(db);
                      tx.Commit();
                  });
            }
            catch (Exception e)
            {
                //TODO: add logging
                return false;
            }

            return true;
        }
        //private static string 
    }
}
