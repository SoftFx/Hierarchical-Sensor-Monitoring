using HSMCommon.Constants;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.Extensions;
using HSMDatabase.LevelDB;
using HSMDatabase.Settings;
using HSMDatabase.SnapshotsDb;
using HSMServer.Core.DataLayer;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMDatabase.DatabaseWorkCore
{
    public sealed class DatabaseCore : IDatabaseCore
    {
        private const int SensorValuesPageCount = 100;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly SensorValuesDatabaseDictionary _sensorValuesDatabases;
        private readonly JournalValuesDatabaseDictionary _journalValuesDatabases;
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly IDatabaseSettings _settings;


        public ISnapshotDatabase Snapshots { get; }


        public long TotalDbSize => _settings.DatabaseFolder.GetSize();

        public long EnviromentDbSize => _settings.PathToEnvironmentDb.GetSize();

        public long SensorHistoryDbSize
        {
            get
            {
                long size = 0;

                foreach (var db in _sensorValuesDatabases)
                    size += db.Name.GetSize();

                return size;
            }
        }


        private delegate IEnumerable<byte[]> GetValuesFunc(ISensorValuesDatabase db);
        private delegate IEnumerable<byte[]> GetJournalValuesFunc(IJournalValuesDatabase db);


        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _logger.Info($"{nameof(DatabaseCore)} is initializing");

            _settings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_settings.PathToEnvironmentDb);
            _sensorValuesDatabases = new SensorValuesDatabaseDictionary(_settings);
            _journalValuesDatabases = new JournalValuesDatabaseDictionary(_settings);
            Snapshots = new SnapshotsDatabase(_settings.PathToSnaphotsDb);

            _logger.Info($"{nameof(DatabaseCore)} initialized");
        }


        #region Fill Sensors (start app)

        public Dictionary<Guid, byte[]> GetLatestValues(Dictionary<Guid, long> sensors)
        {
            var orderedList = sensors.OrderBy(u => u.Value).ToList();
            var result = GetResult(sensors.Keys.ToList());

            var curDb = _sensorValuesDatabases.GetEnumerator();
            var maxBorder = DateTime.MaxValue.Ticks;

            curDb.MoveNext(); //go to first db

            foreach (var (sensorId, time) in orderedList)
                if (time < maxBorder) //skip no data sensors
                {
                    while (curDb.Current != null && curDb.Current.To < time)
                        curDb.MoveNext();

                    if (curDb.Current != null)
                    {
                        var id = sensorId.ToString();

                        result[sensorId] = curDb.Current.Get(BuildSensorValueKey(id, time), Encoding.UTF8.GetBytes(id));
                    }
                    else
                        break;
                }

            return result;
        }

        public Dictionary<Guid, byte[]> GetLatestValuesFrom(Dictionary<Guid, long> sensors)
        {
            var result = GetResult(sensors.Keys.ToList());

            var tempResult = new Dictionary<byte[], (long from, byte[] value)>(sensors.Count);

            foreach (var (id, from) in sensors)
                tempResult.Add(Encoding.UTF8.GetBytes(id.ToString()), (from, null));

            foreach (var database in _sensorValuesDatabases.Reverse())
                database.FillLatestValues(tempResult);

            foreach (var (key, (_, value)) in tempResult)
                result[Guid.Parse(Encoding.UTF8.GetString(key))] = value;

            return result;
        }

        private static Dictionary<Guid, byte[]> GetResult(List<Guid> ids) => ids.ToDictionary(k => k, v => (byte[])null);

        #endregion

        #region Sensors

        public void AddSensor(SensorEntity entity)
        {
            _environmentDatabase.AddSensorIdToList(entity.Id);
            _environmentDatabase.AddSensor(entity);
        }

        public void UpdateSensor(SensorEntity entity) =>
            _environmentDatabase.AddSensor(entity);

        public void ClearSensorValues(string sensorId, DateTime from, DateTime to)
        {
            var fromTicks = from.ToUniversalTime().Ticks;
            var toTicks = to.ToUniversalTime().Ticks;

            var fromBytes = BuildSensorValueKey(sensorId, fromTicks);
            var toBytes = BuildSensorValueKey(sensorId, toTicks);

            foreach (var db in _sensorValuesDatabases)
                if (db.IsInclude(fromTicks, toTicks))
                    db.RemoveSensorValues(fromBytes, toBytes);
        }

        public void RemoveSensorWithMetadata(string sensorId)
        {
            _environmentDatabase.RemoveSensor(sensorId);
            _environmentDatabase.RemoveSensorIdFromList(sensorId);

            ClearSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue);
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

            var databases = _sensorValuesDatabases.Where(db => db.IsInclude(fromTicks, toTicks)).ToList();
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

        #endregion

        #region Policies

        public void AddPolicy(PolicyEntity entity)
        {
            _environmentDatabase.AddPolicyIdToList(new Guid(entity.Id).ToString());
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

        #region Folders

        public void AddFolder(FolderEntity entity)
        {
            _environmentDatabase.AddFolderToList(entity.Id);
            _environmentDatabase.PutFolder(entity);
        }

        public void UpdateFolder(FolderEntity entity) => _environmentDatabase.PutFolder(entity);

        public void RemoveFolder(string id)
        {
            _environmentDatabase.RemoveFolder(id);
            _environmentDatabase.RemoveFolderFromList(id);
        }

        public FolderEntity GetFolder(string id) =>
            _environmentDatabase.GetFolder(id);

        public List<FolderEntity> GetAllFolders()
        {
            var keys = _environmentDatabase.GetFoldersList();

            var foldersEntities = new List<FolderEntity>(keys.Count);
            foreach (var key in keys)
            {
                var folder = _environmentDatabase.GetFolder(key);
                if (folder != null)
                    foldersEntities.Add(folder);
            }

            return foldersEntities;
        }

        #endregion

        #region Environment database : Product

        public void AddProduct(ProductEntity entity)
        {
            _environmentDatabase.AddProductToList(entity.Id);
            _environmentDatabase.PutProduct(entity);
        }

        public void UpdateProduct(ProductEntity entity) => _environmentDatabase.PutProduct(entity);

        public void RemoveProduct(string id)
        {
            _environmentDatabase.RemoveProduct(id);
            _environmentDatabase.RemoveProductFromList(id);
        }

        public ProductEntity GetProduct(string id) => _environmentDatabase.GetProduct(id);

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

        public AccessKeyEntity GetAccessKey(Guid id) => _environmentDatabase.GetAccessKey(id.ToString());

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

        public void AddUser(UserEntity entity) => _environmentDatabase.AddUser(entity);

        public void RemoveUser(UserEntity entity) => _environmentDatabase.RemoveUser(entity);

        public void UpdateUser(UserEntity entity) => AddUser(entity);

        public List<UserEntity> GetUsers() => _environmentDatabase.ReadUsers().ToList();

        public List<UserEntity> GetUsersPage(int page, int pageSize) => _environmentDatabase.ReadUsersPage(page, pageSize).ToList();

        #endregion

        #region Journal

        public void AddJournalValue(JournalKey journalKey, JournalEntity valueEntity)
        {
            var dbs = _journalValuesDatabases.GetNewestDatabases(journalKey.Time);
            
            dbs.Put(journalKey.GetBytes(), valueEntity);
        }

        public void RemoveJournal(Guid id)
        {
            var fromTicks = DateTime.MinValue.Ticks;
            var toTicks = DateTime.MaxValue.Ticks;

            RemoveJournal(id, fromTicks, toTicks, RecordType.Actions);
            RemoveJournal(id, fromTicks, toTicks, RecordType.Changes);
        }

        private void RemoveJournal(Guid id, long fromTicks, long toTicks, RecordType type)
        {
            var fromBytes = new JournalKey(id, fromTicks, type).GetBytes();
            var toBytes = new JournalKey(id, toTicks, type).GetBytes();

            foreach (var db in _journalValuesDatabases)
                if (db.IsInclude(fromTicks, toTicks))
                    db.Remove(fromBytes, toBytes);
        }

        public IAsyncEnumerable<List<byte[]>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType recordType, int count)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var fromBytes = new JournalKey(sensorId, fromTicks, recordType).GetBytes();
            var toBytes = new JournalKey(sensorId, toTicks, recordType).GetBytes();

            var databases = _journalValuesDatabases.Where(db => db.IsInclude(fromTicks, toTicks)).ToList();
            GetJournalValuesFunc getValues = (db) => db.GetValuesFrom(fromBytes, toBytes);

            if (count < 0)
            {
                databases.Reverse();
                getValues = (db) => db.GetValuesTo(fromBytes, toBytes);
            }

            return GetJournalValuesPage(databases, count, getValues);
        }

        private async IAsyncEnumerable<List<byte[]>> GetJournalValuesPage(List<IJournalValuesDatabase> databases, int count, GetJournalValuesFunc getValues)
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

        #endregion

        public void Dispose()
        {
            _environmentDatabase.Dispose();
            _sensorValuesDatabases.ToList().ForEach(d => d.Dispose());
        }
    }
}