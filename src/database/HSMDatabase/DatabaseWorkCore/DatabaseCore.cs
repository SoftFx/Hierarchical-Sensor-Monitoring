using HSMCommon.Constants;
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
        private const int SensorValuesPageCount = 10;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly SensorValuesDatabaseDictionary _sensorValuesDatabases;
        private readonly IDatabaseSettings _databaseSettings;


        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _logger.Info($"{nameof(DatabaseCore)} is initializing");

            _databaseSettings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_databaseSettings.GetPathToEnvironmentDatabase());
            _sensorValuesDatabases = new SensorValuesDatabaseDictionary(_databaseSettings);

            _environmentDatabase.RemoveMonitoringDatabases();

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

            foreach (var database in _sensorValuesDatabases)
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

            dbs.PutSensorValue(valueEntity);
        }

        public List<byte[]> GetSensorValues(string sensorId, DateTime to, int count)
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

        public List<byte[]> GetSensorValues(string sensorId, DateTime from, DateTime to, int count = MaxHistoryCount)
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

        public IEnumerable<List<byte[]>> GetSensorValuesPage(string sensorId, DateTime from, DateTime to, int count)
        {
            var result = new List<byte[]>(SensorValuesPageCount);
            var totalCount = 0;

            var sensorIdBytes = Encoding.UTF8.GetBytes(sensorId);
            var fromBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(sensorId, from.Ticks));
            var toBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(sensorId, to.Ticks));

            foreach (var database in _sensorValuesDatabases.Dbs)
            {
                if (database.To < from.Ticks)
                    continue;

                if (database.From > to.Ticks)
                    yield break;

                foreach (var value in database.GetValue(sensorIdBytes, fromBytes, toBytes))
                {
                    result.Add(value);
                    totalCount++;

                    if (result.Count == SensorValuesPageCount)
                    {
                        yield return result;

                        result.Clear();
                    }

                    if (count == totalCount)
                        yield break;
                }
            }

            yield return result;
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

        [Obsolete("Remove this after product id migration")]
        public void UpdateProduct(string oldId, ProductEntity entity)
        {
            _environmentDatabase.RemoveProductFromList(oldId);

            AddProduct(entity);
        }

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

        public void Dispose()
        {
            _environmentDatabase.Dispose();
            _sensorValuesDatabases.ToList().ForEach(d => d.Dispose());
        }
    }
}