using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HSMServer.Configuration;
using HSMServer.Model;
using LightningDB;
using Microsoft.EntityFrameworkCore.Internal;
using HSMCommon.DataObjects;

namespace HSMServer.Database
{
    public static class DataStorage
    {
        private const string ENVIRONMENT_PATH = @"Database";
        private const string DATABASE_NAME = "monitoring";
        private static LightningEnvironment environment;
        public static void Initialize()
        {
            environment = new LightningEnvironment(ENVIRONMENT_PATH);
            environment.MaxDatabases = 1;
            environment.MaxReaders = Config.UsersCount;
            environment.Open();
            using var tx = environment.BeginTransaction();
            using (var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration {Flags = DatabaseOpenFlags.Create}))
            {
                tx.Commit();
            }
        }

        public static void DisposeDatabase()
        {
            environment.Dispose();
            environment = null;
        }

        public static ReturnCodes PutData(SensorData sensorData)
        {
            if (!Config.IsKeyRegistered(sensorData.Key))
            {
                return ReturnCodes.IncorrectKey;
            }
            ShortSensorData resultObject = new ShortSensorData { Comment = sensorData.Comment, Success = sensorData.Success, Time = sensorData.Time };

            using var tx = environment.BeginTransaction();
            using var db = tx.OpenDatabase(DATABASE_NAME, new DatabaseConfiguration {Flags = DatabaseOpenFlags.None});

            string json = JsonSerializer.Serialize(resultObject);
            string keyString = Config.GenerateDataStorageKey(sensorData.Key);
            using (var cursor = tx.CreateCursor(db))
            {
                cursor.Put(Encoding.ASCII.GetBytes(keyString), Encoding.ASCII.GetBytes(json), CursorPutOptions.NoOverwrite);
            }

            var count = tx.GetEntriesCount(db);
            tx.Commit();
            
            return ReturnCodes.Success;
        }

        public static ReturnCodes GetSensorsData(string machineName, string sensorName,
            out List<ShortSensorData> result)
        {
            string keyString = Config.GenerateSearchKey(machineName, sensorName);
            if (string.IsNullOrEmpty(keyString))
            {
                result = null;
                return ReturnCodes.IncorrectKey;
            }

            return ReadSensorsData(keyString, out result);
        }
        public static ReturnCodes GetSensorsData(string machineName, out List<ShortSensorData> result)
        {
            string keyString = Config.GenerateSearchKey(machineName);
            if (string.IsNullOrEmpty(keyString))
            {
                result = null;
                return ReturnCodes.IncorrectKey;
            }

            return ReadSensorsData(keyString, out result);
        }

        private static ReturnCodes ReadSensorsData(string keyString, out List<ShortSensorData> result)
        {
            using var tx = environment.BeginTransaction(TransactionBeginFlags.ReadOnly);
            using var db = tx.OpenDatabase(DATABASE_NAME);

            //var count = tx.GetEntriesCount(db);

            result = new List<ShortSensorData>();
            byte[] bytesKey = Encoding.ASCII.GetBytes(keyString);

            using (var cursor = tx.CreateCursor(db))
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
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                } while (cursor.Next() == MDBResultCode.Success);
            }
            return ReturnCodes.Success;
        }
    }
}
