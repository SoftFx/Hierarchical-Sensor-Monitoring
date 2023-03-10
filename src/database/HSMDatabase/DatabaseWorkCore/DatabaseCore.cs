using HSMCommon.Constants;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.Settings;
using HSMServer.Core.Configuration;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Registration;
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
        private const int SensorValuesPageCount = 100;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly SensorValuesDatabaseDictionary _sensorValuesDatabases;
        private readonly IDatabaseSettings _databaseSettings;

        private delegate IEnumerable<byte[]> GetValuesFunc(ISensorValuesDatabase db);


        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _logger.Info($"{nameof(DatabaseCore)} is initializing");

            _databaseSettings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_databaseSettings.GetPathToEnvironmentDatabase());
            _sensorValuesDatabases = new SensorValuesDatabaseDictionary(_databaseSettings);

            _logger.Info($"{nameof(DatabaseCore)} initialized");
        }


        #region Database size

        public long GetDatabaseSize()
        {
            var databasesDir = new DirectoryInfo(_databaseSettings.DatabaseFolder);
            return GetDirectorySize(databasesDir);
        }

        public long GetSensorsHistoryDatabaseSize()
        {
            long size = 0;

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

            var tempResult = new Dictionary<byte[], (Guid sensorId, byte[] value)>(sensors.Count);

            foreach (var sensor in sensors)
            {
                if (result[sensor.Id] != null)
                    continue;

                byte[] key = Encoding.UTF8.GetBytes(sensor.Id.ToString());
                (Guid sensorId, byte[] latestValue) value = (sensor.Id, null);

                tempResult.Add(key, value);
            }

            foreach (var database in _sensorValuesDatabases.Reverse())
                database.FillLatestValues(tempResult);

            foreach (var (_, (sensorId, value)) in tempResult)
                if (value != null)
                    result[sensorId] = value;

            return result;
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

        public void ClearSensorValues(string sensorId) =>
            RemoveSensorValues(sensorId);

        public void RemoveSensorWithMetadata(string sensorId)
        {
            _environmentDatabase.RemoveSensor(sensorId);
            _environmentDatabase.RemoveSensorIdFromList(sensorId);

            RemoveSensorValues(sensorId);
        }

        public void AddSensorValue(SensorValueEntity valueEntity)
        {
            var dbs = _sensorValuesDatabases.GetNewestDatabases(valueEntity.ReceivingTime);
            var key = BuildSensorValueKey(valueEntity.SensorId, valueEntity.ReceivingTime);

            dbs.PutSensorValue(key, valueEntity.Value);
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

        public IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(string sensorId, DateTime from, DateTime to, int count)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var fromBytes = BuildSensorValueKey(sensorId, fromTicks);
            var toBytes = BuildSensorValueKey(sensorId, toTicks);

            var databases = _sensorValuesDatabases.Where(db => fromTicks <= db.To && toTicks >= db.From).ToList();
            GetValuesFunc getValues = (db) => db.GetValuesFrom(fromBytes, toBytes);

            if (count < 0)
            {
                databases.Reverse();
                getValues = (db) => db.GetValuesTo(fromBytes, toBytes);
            }

            return GetSensorValuesPage(databases, count, getValues);
        }

        private async IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(List<ISensorValuesDatabase> databases, int count, GetValuesFunc getValues)
        {
            var result = new List<byte[]>(SensorValuesPageCount);
            var totalCount = 0;

            foreach (var database in databases)
            {
                foreach (var value in getValues(database))
                {
                    result.Add(value);
                    totalCount++;

                    if (result.Count == SensorValuesPageCount)
                    {
                        yield return result;

                        result.Clear();
                    }

                    if (Math.Abs(count) == totalCount)
                    {
                        yield return result;
                        yield break;
                    }
                }
            }

            yield return result;
        }

        // "D19" string format is for inserting leading zeros (long.MaxValue has 19 symbols)
        private static byte[] BuildSensorValueKey(string sensorId, long time) =>
            Encoding.UTF8.GetBytes($"{sensorId}_{time:D19}");

        private void RemoveSensorValues(string sensorId)
        {
            foreach (var db in _sensorValuesDatabases)
                db.RemoveSensorValues(sensorId);
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

        public void AddUser(UserEntity entity) =>
            _environmentDatabase.AddUser(entity);

        public void RemoveUser(UserEntity entity) =>
            _environmentDatabase.RemoveUser(entity);

        public void UpdateUser(UserEntity entity) => AddUser(entity);

        public List<UserEntity> GetUsers() => _environmentDatabase.ReadUsers().ToList();

        public List<UserEntity> GetUsersPage(int page, int pageSize) =>
            _environmentDatabase.ReadUsersPage(page, pageSize).ToList();

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

        public void Dispose()
        {
            _environmentDatabase.Dispose();
            _sensorValuesDatabases.ToList().ForEach(d => d.Dispose());
        }
    }
}