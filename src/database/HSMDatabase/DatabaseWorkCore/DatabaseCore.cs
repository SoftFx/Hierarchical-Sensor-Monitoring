﻿using HSMCommon.Constants;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.Settings;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HSMDatabase.DatabaseWorkCore
{
    public sealed class DatabaseCore : IDatabaseCore
    {
        private const int MaxHistoryCount = 50000;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly ITimeDatabaseDictionary _sensorsDatabases;
        private readonly SensorValuesDatabaseDictionary _sensorValuesDatabases;
        private readonly IDatabaseSettings _databaseSettings;


        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _logger.Info($"{nameof(DatabaseCore)} is initializing");

            _databaseSettings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_databaseSettings.GetPathToEnvironmentDatabase());
            _sensorsDatabases = new TimeDatabaseDictionary();
            _sensorValuesDatabases = new SensorValuesDatabaseDictionary(_databaseSettings);

            OpenAllExistingSensorDatabases();

            _logger.Info($"{nameof(DatabaseCore)} initialized");
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

            foreach (var db in _sensorValuesDatabases)
                size += GetDirectorySize(new DirectoryInfo(db.Name));

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

        #region Fill Sensors (start app)

        public Dictionary<Guid, byte[]> GetLatestValues(List<BaseSensorModel> sensors)
        {
            var result = new Dictionary<Guid, byte[]>(sensors.Count);

            foreach (var sensor in sensors)
                result.Add(sensor.Id, null);

            FillLatestValues(sensors, result);

            return FillRemainingLatestValues(sensors, result);
        }

        private Dictionary<Guid, byte[]> FillLatestValues(List<BaseSensorModel> sensors, Dictionary<Guid, byte[]> result)
        {
            var tempResult = new Dictionary<byte[], (Guid sensorId, byte[] value)>(sensors.Count);

            foreach (var sensor in sensors)
            {
                if (result[sensor.Id] != null)
                    continue;

                byte[] key = Encoding.UTF8.GetBytes(sensor.Id.ToString());
                (Guid sensorId, byte[] latestValue) value = (sensor.Id, null);

                tempResult.Add(key, value);
            }

            foreach (var database in _sensorValuesDatabases)
                database.FillLatestValues(tempResult);

            foreach (var (_, (sensorId, value)) in tempResult)
                if (value != null)
                    result[sensorId] = value;

            return result;
        }

        private Dictionary<Guid, byte[]> FillRemainingLatestValues(List<BaseSensorModel> sensors, Dictionary<Guid, byte[]> latestValues)
        {
            var remainingResult = new Dictionary<byte[], (Guid sensorId, byte[] value)>(sensors.Count);

            foreach (var sensor in sensors)
            {
                if (latestValues[sensor.Id] != null)
                    continue;

                byte[] key = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorReadValueKey(sensor.RootProductName, sensor.Path));
                (Guid sensorId, byte[] latestValue) value = (sensor.Id, null);

                remainingResult.Add(key, value);
            }

            var databases = _sensorsDatabases.GetAllDatabases();
            databases.Reverse();

            foreach (var database in databases)
                database.FillLatestValues(remainingResult);

            foreach (var (_, (sensorId, value)) in remainingResult)
                if (value != null)
                    latestValues[sensorId] = value;

            return latestValues;
        }

        #endregion

        #region Sensors

        public void AddSensor(SensorEntity entity)
        {
            _environmentDatabase.AddSensorIdToList(entity.Id);
            _environmentDatabase.AddSensor(entity);
        }

        public void UpdateSensor(SensorEntity entity) =>
            _environmentDatabase.AddSensor(entity);

        public void ClearSensorValues(string sensorId, string productName, string path)
        {
            RemoveSensorData(productName, path);
            RemoveSensorValues(sensorId);
        }

        public void RemoveSensorWithMetadata(string sensorId, string productName, string path)
        {
            _environmentDatabase.RemoveSensor(sensorId);
            _environmentDatabase.RemoveSensorIdFromList(sensorId);

            RemoveSensorData(productName, path);
            RemoveSensorValues(sensorId);
        }

        public void AddSensorValue(SensorValueEntity valueEntity)
        {
            var dbs = _sensorValuesDatabases.GetNewestDatabases(valueEntity.ReceivingTime);

            dbs.PutSensorValue(valueEntity);
        }

        public List<byte[]> GetSensorValues(string sensorId, string productName, string path, DateTime to, int count)
        {
            var result = GetSensorValues(sensorId, to, count);

            var remainingCount = count - result.Count;
            if (remainingCount > 0)
                result.AddRange(GetSensorValues(productName, path, to, remainingCount));

            return result;
        }

        public List<byte[]> GetSensorValues(string sensorId, string productName, string path, DateTime from, DateTime to, int count = MaxHistoryCount)
        {
            var result = GetSensorValues(sensorId, from, to, count);
            if (result.Count < count)
                result.AddRange(GetSensorValues(productName, path, from, to, count - result.Count));

            return result;
        }

        private void RemoveSensorValues(string sensorId)
        {
            foreach (var db in _sensorValuesDatabases)
                db.RemoveSensorValues(sensorId);
        }

        private void RemoveSensorData(string productName, string path)
        {
            //TAM-90: Do not delete metadata when delete sensors
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
                database.DeleteAllSensorValues(productName, path);
        }

        private List<byte[]> GetSensorValues(string sensorId, DateTime to, int count)
        {
            var toBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(sensorId, to.Ticks));
            var result = new List<byte[]>(count);

            foreach (var database in _sensorValuesDatabases)
            {
                result.AddRange(database.GetValues(sensorId, toBytes, count - result.Count));

                if (count == result.Count)
                    break;
            }

            return result;
        }

        private List<byte[]> GetSensorValues(string productName, string path, DateTime to, int count)
        {
            var result = new List<byte[]>(count);

            var databases = _sensorsDatabases.GetSortedDatabases();
            foreach (var database in databases)
            {
                result.AddRange(database.GetSensorValues(productName, path, to, count - result.Count));

                if (count == result.Count)
                    break;
            }

            return result;
        }

        private List<byte[]> GetSensorValues(string sensorId, DateTime from, DateTime to, int count)
        {
            var result = new List<byte[]>(Math.Min(MaxHistoryCount, count));

            var fromBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(sensorId, from.Ticks));
            var toBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(sensorId, to.Ticks));

            foreach (var database in _sensorValuesDatabases)
            {
                if (database.To < from.Ticks || database.From > to.Ticks)
                    continue;

                result.AddRange(database.GetValues(sensorId, fromBytes, toBytes, count - result.Count));

                if (count == result.Count)
                    break;
            }

            return result;
        }

        private List<byte[]> GetSensorValues(string productName, string path, DateTime from, DateTime to, int count)
        {
            var result = new List<byte[]>(Math.Min(MaxHistoryCount, count));

            var databases = _sensorsDatabases.GetSortedDatabases();
            foreach (var database in databases)
            {
                if (database.DatabaseMaxTicks < from.Ticks || database.DatabaseMinTicks > to.Ticks)
                    continue;

                result.AddRange(database.GetSensorValuesBytesBetween(productName, path, from, to, count - result.Count));

                if (count == result.Count)
                    break;
            }

            return result;
        }

        public List<SensorEntity> GetAllSensors()
        {
            var sensorsIds = _environmentDatabase.GetAllSensorsIds();

            var sensorEntities = new List<SensorEntity>(sensorsIds.Count);
            foreach (var sensorId in sensorsIds)
            {
                var sensor = _environmentDatabase.GetSensorEntity(sensorId);
                if (sensor != null)
                    sensorEntities.Add(sensor);
            }

            return sensorEntities;
        }

        #endregion

        #region Policies

        public void AddPolicy(PolicyEntity entity)
        {
            _environmentDatabase.AddPolicyIdToList(entity.Id);
            _environmentDatabase.AddPolicy(entity);
        }

        public void UpdatePolicy(PolicyEntity entity) => _environmentDatabase.AddPolicy(entity);

        public void RemovePolicy(Guid id)
        {
            var strId = id.ToString();

            _environmentDatabase.RemovePolicyFromList(strId);
            _environmentDatabase.RemovePolicy(strId);
        }

        public List<byte[]> GetAllPolicies()
        {
            var policiesIds = _environmentDatabase.GetAllPoliciesIds();

            var policies = new List<byte[]>(policiesIds.Count);
            foreach (var policyId in policiesIds)
            {
                var policy = _environmentDatabase.GetPolicy(policyId);
                if (policy != null && policy.Length != 0)
                    policies.Add(policy);
            }

            return policies;
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

        #region AccessKey

        public void RemoveAccessKey(Guid id)
        {
            var strId = id.ToString();

            _environmentDatabase.RemoveAccessKey(strId);
            _environmentDatabase.RemoveAccessKeyFromList(strId);
        }

        public void AddAccessKey(AccessKeyEntity entity)
        {
            _environmentDatabase.AddAccessKeyToList(entity.Id);
            _environmentDatabase.AddAccessKey(entity);
        }

        public void UpdateAccessKey(AccessKeyEntity entity) => AddAccessKey(entity);

        public AccessKeyEntity GetAccessKey(Guid id) =>
            _environmentDatabase.GetAccessKey(id.ToString());

        public List<AccessKeyEntity> GetAccessKeys()
        {
            var keys = new List<AccessKeyEntity>();
            var keyIds = _environmentDatabase.GetAccessKeyList();

            foreach (var keyId in keyIds)
            {
                var keyEntity = _environmentDatabase.GetAccessKey(keyId);
                if (keyEntity != null)
                    keys.Add(keyEntity);
            }

            return keys;
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
            _sensorValuesDatabases.ToList().ForEach(d => d.Dispose());
        }
    }
}