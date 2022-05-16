﻿using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.Settings;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore
{
    public sealed class DatabaseCore : IDatabaseCore
    {
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly ITimeDatabaseDictionary _sensorsDatabases;
        private readonly IDatabaseSettings _databaseSettings;

        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _databaseSettings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_databaseSettings.GetPathToEnvironmentDatabase());
            _sensorsDatabases = new TimeDatabaseDictionary(_environmentDatabase, dbSettings ?? new DatabaseSettings());

            OpenAllExistingSensorDatabases();
        }

        private void OpenAllExistingSensorDatabases()
        {
            List<string> databasesList = _environmentDatabase.GetMonitoringDatabases();
            foreach (var databaseName in databasesList)
            {
                GetDatesFromFolderName(databaseName, out DateTime from, out DateTime to);
                if (from != DateTime.MinValue && to != DateTime.MinValue)
                {
                    ISensorsDatabase database = LevelDBManager.GetSensorDatabaseInstance(
                        _databaseSettings.GetPathToMonitoringDatabase(databaseName), from, to);
                    _sensorsDatabases.AddDatabase(database);
                }
            }
        }

        #region Database size

        public long GetDatabaseSize()
        {
            var databasesDir = new DirectoryInfo(_databaseSettings.DatabaseFolder);
            return GetDirectorySize(databasesDir);
        }

        public long GetMonitoringDataSize()
        {
            long size = 0;
            var databasesList = _environmentDatabase.GetMonitoringDatabases();
            foreach (var monitoringDB in databasesList)
            {
                var info = new DirectoryInfo(_databaseSettings.GetPathToMonitoringDatabase(monitoringDB));
                size += GetDirectorySize(info);
            }

            return size;
        }

        public long GetEnvironmentDatabaseSize()
        {
            DirectoryInfo environmentDatabaseDir = new DirectoryInfo(_databaseSettings.GetPathToEnvironmentDatabase());
            return GetDirectorySize(environmentDatabaseDir);
        }

        private static long GetDirectorySize(DirectoryInfo directory)
        {
            long size = 0;
            FileInfo[] files = directory.GetFiles();
            foreach (var file in files)
            {
                try
                {
                    size += file.Length;
                }
                catch { }
            }

            DirectoryInfo[] directories = directory.GetDirectories();
            foreach (var dir in directories)
            {
                size += GetDirectorySize(dir);
            }

            return size;
        }

        #endregion

        #region Sensors methods

        public List<SensorDataEntity> GetAllSensorData(string productName, string path)
        {
            var result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                result.AddRange(database.GetAllSensorValues(productName, path));
            }

            return result;
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, int n)
        {
            var result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            databases.Reverse();
            foreach (var database in databases)
            {
                result.AddRange(database.GetAllSensorValues(productName, path));
                if (result.Count >= n)
                    break;
            }

            result.Sort((d1, d2) =>
                d1.TimeCollected.CompareTo(d2.TimeCollected));
            return result.TakeLast(n).ToList();
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from)
        {
            var result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                //Skip too old data
                if (database.DatabaseMaxTicks < from.Ticks)
                    continue;

                result.AddRange(database.GetSensorValuesFrom(productName, path, from));
            }

            result.RemoveAll(r => r.TimeCollected < from);
            return result;
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from, DateTime to)
        {
            var result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                //Skip too old data
                if (database.DatabaseMaxTicks < from.Ticks)
                    continue;

                //Skip too new data
                if (database.DatabaseMinTicks > to.Ticks)
                    continue;

                result.AddRange(database.GetSensorValuesBetween(productName, path, from, to));
            }

            return result;
        }

        public SensorDataEntity GetLatestSensorValue(string productName, string path)
        {
            List<ISensorsDatabase> databases = _sensorsDatabases.GetAllDatabases();
            databases.Reverse();
            foreach (var database in databases)
            {
                var currentLatestValue = database.GetLatestSensorValue(productName, path);
                if (currentLatestValue != null)
                {
                    return currentLatestValue;
                }
            }

            return null;
        }

        #endregion

        #region Environment database : Sensor

        public void AddSensor(SensorEntity entity) =>
            _environmentDatabase.AddSensor(entity);

        public void RemoveSensor(string productName, string path)
        {
            //TAM-90: Do not delete metadata when delete sensors
            //_environmentDatabase.RemoveSensor(productName, path);
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                database.DeleteAllSensorValues(productName, path);
            }
        }

        public void RemoveSensorWithMetadata(string productName, string path)
        {
            _environmentDatabase.RemoveSensor(productName, path);

            RemoveSensor(productName, path);
        }

        public void UpdateSensor(SensorEntity entity) => AddSensor(entity);

        public void PutSensorData(SensorDataEntity entity, string productName)
        {
            var database = _sensorsDatabases.GetDatabase(entity.TimeCollected);
            database.PutSensorData(entity, productName);
        }

        public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        {
            SensorDataEntity entity = GetLatestSensorValue(productName, path);
            return entity != null ? entity.ConvertToHistoryData() : null;
        }

        public List<SensorHistoryData> GetAllSensorHistory(string productName, string path) =>
            GetSensorHistoryDatas(GetAllSensorData(productName, path));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from) =>
            GetSensorHistoryDatas(GetSensorData(productName, path, from));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to) =>
            GetSensorHistoryDatas(GetSensorData(productName, path, from, to));

        public List<SensorHistoryData> GetSensorHistory(string productName, string path, int n) =>
            GetSensorHistoryDatas(GetSensorData(productName, path, n));

        private static List<SensorHistoryData> GetSensorHistoryDatas(List<SensorDataEntity> history)
        {
            var historyCount = history?.Count ?? 0;

            var historyDatas = new List<SensorHistoryData>(historyCount);
            if (historyCount != 0)
                historyDatas.AddRange(history.Select(h => h.ConvertToHistoryData()));

            return historyDatas;
        }

        public List<SensorEntity> GetAllSensors()
        {
            var oldEntities = _environmentDatabase.GetSensorsInfo();
            if (oldEntities == null || oldEntities.Count == 0)
                return new List<SensorEntity>();

            foreach (var oldEntity in oldEntities)
            {
                if (!string.IsNullOrEmpty(oldEntity.Id))
                    continue;

                oldEntity.Id = Guid.NewGuid().ToString();
                oldEntity.ProductId = string.Empty;
                oldEntity.IsConverted = true;
            }

            return oldEntities;
        }

        #endregion

        #region Environment database : Product

        public void AddProduct(ProductEntity entity)
        {
            _environmentDatabase.AddProductToList(entity.Id);
            _environmentDatabase.PutProduct(entity);
        }

        public void UpdateProduct(ProductEntity entity) =>
            _environmentDatabase.PutProduct(entity);

        public void RemoveProduct(string id)
        {
            _environmentDatabase.RemoveProduct(id);
            _environmentDatabase.RemoveProductFromList(id);
        }

        public ProductEntity GetProduct(string id) =>
            _environmentDatabase.GetProduct(id);

        public List<ProductEntity> GetAllProducts()
        {
            var keys = _environmentDatabase.GetProductsList();

            var productEntities = new List<ProductEntity>(keys.Count);
            foreach (var key in keys)
            {
                var product = _environmentDatabase.GetProduct(key);
                if (product != null)
                    productEntities.Add(product);
            }

            return productEntities;
        }

        #endregion

        #region Environment database : User

        public void AddUser(User user)
        {
            UserEntity entity = user.ConvertToEntity();
            _environmentDatabase.AddUser(entity);
        }

        public void RemoveUser(User user)
        {
            UserEntity entity = user.ConvertToEntity();
            _environmentDatabase.RemoveUser(entity);
        }

        public void UpdateUser(User user) => AddUser(user);

        public List<User> GetUsers() => GetUsers(_environmentDatabase.ReadUsers().ToList());

        public List<User> GetUsersPage(int page, int pageSize) =>
            GetUsers(_environmentDatabase.ReadUsersPage(page, pageSize).ToList());

        private static List<User> GetUsers(List<UserEntity> userEntities)
        {
            var userEntitiesCount = userEntities?.Count ?? 0;
            var users = new List<User>(userEntitiesCount);

            if (userEntitiesCount != 0)
                users.AddRange(userEntities.Select(e => new User(e)));

            return users;
        }

        #endregion

        #region Environment database : Configuration

        public ConfigurationObject GetConfigurationObject(string name)
        {
            ConfigurationEntity entity = _environmentDatabase.ReadConfigurationObject(name);
            return entity != null ? new ConfigurationObject(entity) : null;
        }

        public void WriteConfigurationObject(ConfigurationObject obj)
        {
            ConfigurationEntity entity = obj.ConvertToEntity();
            _environmentDatabase.WriteConfigurationObject(entity);
        }

        public void RemoveConfigurationObject(string name) =>
            _environmentDatabase.RemoveConfigurationObject(name);

        #endregion

        #region Environment database : Ticket

        public RegistrationTicket ReadRegistrationTicket(Guid id)
        {
            var entity = _environmentDatabase.ReadRegistrationTicket(id);
            return entity != null ? new RegistrationTicket(entity) : null;
        }

        public void RemoveRegistrationTicket(Guid id) =>
            _environmentDatabase.RemoveRegistrationTicket(id);

        public void WriteRegistrationTicket(RegistrationTicket ticket)
        {
            RegisterTicketEntity entity = ticket.ConvertToEntity();
            _environmentDatabase.WriteRegistrationTicket(entity);
        }

        #endregion

        #region Private methods

        private static void GetDatesFromFolderName(string folder, out DateTime from, out DateTime to)
        {
            var splitResults = folder.Split('_');

            bool isFromTicks = long.TryParse(splitResults[1], out long fromTicks);
            from = isFromTicks ? new DateTime(fromTicks) : DateTime.MinValue;

            bool isToTicks = long.TryParse(splitResults[2], out long toTicks);
            to = isToTicks ? new DateTime(toTicks) : DateTime.MinValue;
        }

        #endregion

        public void Dispose()
        {
            _environmentDatabase.Dispose();
            _sensorsDatabases.GetAllDatabases().ForEach(d => d.Dispose());
        }
    }
}