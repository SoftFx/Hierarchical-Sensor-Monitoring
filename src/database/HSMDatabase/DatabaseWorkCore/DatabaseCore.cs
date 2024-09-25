using HSMCommon.Constants;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
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
using System.Text.Json;

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


        public IDashboardCollection Dashboards { get; }

        public ISnapshotDatabase Snapshots { get; }


        public long TotalDbSize => _settings.DatabaseFolder.GetSize();

        public long ConfigDbSize => _settings.PathToEnvironmentDb.GetSize() + _settings.PathToServerLayoutDb.GetSize() + Snapshots.Size;

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

        public long JournalDbSize => _settings.PathToJournalDb.GetSize();

        public long BackupsSize => _settings.DatabaseBackupsFolder.GetSize();


        private delegate IEnumerable<byte[]> GetValuesFunc(ISensorValuesDatabase db);
        private delegate IEnumerable<(byte[], byte[])> GetJournalValuesFunc(IJournalValuesDatabase db);


        public DatabaseCore(IDatabaseSettings dbSettings = null)
        {
            _logger.Info($"{nameof(DatabaseCore)} is initializing");

            _settings = dbSettings ?? new DatabaseSettings();
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_settings.PathToEnvironmentDb);
            _sensorValuesDatabases = new SensorValuesDatabaseDictionary(_settings);
            _journalValuesDatabases = new JournalValuesDatabaseDictionary(_settings);

            Dashboards = new DashboardCollection(_settings.PathToServerLayoutDb);
            Snapshots = new SnapshotsDatabase(_settings.PathToSnaphotsDb);

            _logger.Info($"{nameof(DatabaseCore)} initialized");
        }


        public TaskResult<string> BackupEnvironment(string backupPath) => _environmentDatabase.Backup(backupPath);


        #region Fill Sensors (start app)


        public byte[] GetLatestValue(Guid sensorId, long to)
        {
            var maxKey = BuildSensorValueKey(sensorId.ToString(), to);
            var idBytes = Encoding.UTF8.GetBytes(sensorId.ToString());

            foreach (var database in _sensorValuesDatabases)
                if (database.From <= to)
                {
                    var value = database.GetLatest(maxKey, idBytes);

                    if (value is not null)
                        return value;
                }

            return null;
        }


        public Dictionary<Guid, byte[]> GetLatestValues(Dictionary<Guid, long> sensors)
        {
            var orderedList = sensors.OrderByDescending(u => u.Value).ToList();
            var result = GetResult(sensors.Keys.ToList());

            var curDb = _sensorValuesDatabases.GetEnumerator();
            var maxBorder = DateTime.MaxValue.Ticks;

            var dbExist = curDb.MoveNext(); //go to first db

            foreach (var (sensorId, time) in orderedList)
                if (time < maxBorder) //skip no data sensors
                {
                    while (dbExist && !curDb.Current.IsInclude(time))
                        dbExist = curDb.MoveNext();

                    if (dbExist)
                    {
                        var id = sensorId.ToString();

                        result[sensorId] = curDb.Current.Get(BuildSensorValueKey(id, time), Encoding.UTF8.GetBytes(id));
                    }
                    else
                        break;
                }

            return result;
        }

        public Dictionary<Guid, byte[]> GetLatestValuesFromTo(Dictionary<Guid, (long, long)> sensors)
        {
            var result = GetResult(sensors.Keys.ToList());

            var tempResult = new Dictionary<byte[], (long from, byte[] to, byte[] value)>(sensors.Count);

            foreach (var (id, (from, to)) in sensors)
                tempResult.Add(Encoding.UTF8.GetBytes(id.ToString()), (from, BuildSensorValueKey(id.ToString(), to), null));

            foreach (var database in _sensorValuesDatabases)
                database.FillLatestValues(tempResult);

            foreach (var (key, (_, _, value)) in tempResult)
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
            {
                if (db.IsInclude(fromTicks, toTicks))
                    db.RemoveSensorValues(fromBytes, toBytes);
                else if (db.To < fromTicks)
                    break;
            }
        }

        public (long dateCnt, long keySize, long valueSize) CalculateSensorHistorySize(Guid sensorId)
        {
            var fromKey = BuildSensorValueKey(sensorId, DateTime.MinValue.Ticks);
            var toKey = BuildSensorValueKey(sensorId, DateTime.MaxValue.Ticks);

            var totalDataCnt = 0L;
            var totalKeySize = 0L;
            var totalValueSize = 0L;

            foreach (var db in _sensorValuesDatabases)
                foreach (var (keyBytes, valueBytes) in db.GetKeysValuesTo(fromKey, toKey))
                {
                    totalKeySize += keyBytes.Length;
                    totalValueSize += valueBytes.Length;
                    totalDataCnt++;
                }

            return (totalDataCnt, totalKeySize, totalValueSize);
        }

        public void RemoveSensorWithMetadata(string sensorId)
        {
            _environmentDatabase.RemoveSensor(sensorId);
            _environmentDatabase.RemoveSensorIdFromList(sensorId);

            ClearSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue);
        }

        public void AddSensorValue(SensorValueEntity valueEntity)
        {
            var dbs = _sensorValuesDatabases.GetDatabaseByTime(valueEntity.ReceivingTime);
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

        public IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var fromBytes = BuildSensorValueKey(sensorId.ToString(), fromTicks);
            var toBytes = BuildSensorValueKey(sensorId.ToString(), toTicks);

            var databases = _sensorValuesDatabases.Where(db => db.IsInclude(fromTicks, toTicks)).ToList();
            GetValuesFunc getValues = (db) => db.GetValuesTo(fromBytes, toBytes);

            if (count > 0)
            {
                databases.Reverse();
                getValues = (db) => db.GetValuesFrom(fromBytes, toBytes);
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


        private static byte[] BuildSensorValueKey(Guid sensorId, long time) =>
            BuildSensorValueKey(sensorId.ToString(), time);

        // "D19" string format is for inserting leading zeros (long.MaxValue has 19 symbols)
        private static byte[] BuildSensorValueKey(string sensorId, long time) =>
            Encoding.UTF8.GetBytes($"{sensorId}_{time:D19}");

        #endregion

        #region Policies

        public void AddPolicy(PolicyEntity entity)
        {
            _environmentDatabase.AddPolicyIdToList(new Guid(entity.Id));
            _environmentDatabase.AddPolicy(entity);
        }

        public void UpdatePolicy(PolicyEntity entity) => _environmentDatabase.AddPolicy(entity);

        public void RemovePolicy(Guid id) => _environmentDatabase.RemovePolicy(id);

        public List<PolicyEntity> GetAllPolicies()
        {
            var policiesIds = _environmentDatabase.GetAllPoliciesIds();

            var policies = new List<PolicyEntity>(policiesIds.Count);

            foreach (var policyId in policiesIds)
            {
                var policy = _environmentDatabase.GetPolicy(policyId);

                if (policy != null)
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

        #region Environment database : Telegram chat

        public void AddTelegramChat(TelegramChatEntity chat)
        {
            _environmentDatabase.AddTelegramChatToList(chat.Id);
            _environmentDatabase.AddTelegramChat(chat);
        }

        public void UpdateTelegramChat(TelegramChatEntity chat) => _environmentDatabase.AddTelegramChat(chat);

        public void RemoveTelegramChat(byte[] chatId)
        {
            _environmentDatabase.RemoveTelegramChat(chatId);
            _environmentDatabase.RemoveTelegramChatFromList(chatId);
        }

        public TelegramChatEntity GetTelegramChat(byte[] chatId) => _environmentDatabase.GetTelegramChat(chatId);

        public List<TelegramChatEntity> GetTelegramChats()
        {
            var chats = new List<TelegramChatEntity>(1 << 4);
            var ids = _environmentDatabase.GetTelegramChatsList();

            foreach (var id in ids)
            {
                var keyEntity = _environmentDatabase.GetTelegramChat(id);
                if (keyEntity != null)
                    chats.Add(keyEntity);
            }

            return chats;
        }

        #endregion

        #region Journal

        public void AddJournalValue(JournalKey journalKey, JournalRecordEntity value)
        {
            var dbs = _journalValuesDatabases.GetNewestDatabases(journalKey.Time);

            dbs.Put(journalKey.GetBytes(), value);
        }

        public void RemoveJournalValues(Guid id, Guid parentId)
        {
            var fromTicks = DateTime.MinValue.Ticks;
            var toTicks = DateTime.MaxValue.Ticks;

            var fromBytes = new JournalKey(id, fromTicks, RecordType.Actions).GetBytes();
            var toBytes = new JournalKey(id, toTicks, RecordType.Changes).GetBytes();

            foreach (var db in _journalValuesDatabases)
                if (db.IsInclude(fromTicks, toTicks))
                {
                    PutRecordsToParent(fromBytes, toBytes, parentId, db);

                    db.Remove(fromBytes, toBytes);
                }
        }

        public IAsyncEnumerable<List<(byte[] Key, JournalRecordEntity Entity)>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType types, int count)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            IEnumerable<(byte[], byte[])> GetValuesEnumerator(IJournalValuesDatabase db, Func<byte[], byte[], IEnumerable<(byte[], byte[])>> requestDb)
            {
                foreach (var recordType in Enum.GetValues<RecordType>())
                    if (types.HasFlag(recordType))
                    {
                        var fromBytes = new JournalKey(sensorId, fromTicks, recordType).GetBytes();
                        var toBytes = new JournalKey(sensorId, toTicks, recordType).GetBytes();

                        foreach (var t in requestDb(fromBytes, toBytes))
                            yield return t;
                    }
            }

            var databases = _journalValuesDatabases.Where(db => db.IsInclude(fromTicks, toTicks)).ToList();
            GetJournalValuesFunc getValues = (db) => GetValuesEnumerator(db, db.GetValuesFrom);

            if (count < 0)
            {
                databases.Reverse();
                getValues = (db) => GetValuesEnumerator(db, db.GetValuesTo);
            }

            return GetJournalValuesPage(databases, count, getValues);
        }


        private static void PutRecordsToParent(byte[] from, byte[] to, Guid id, IJournalValuesDatabase db)
        {
            if (id != default)
                foreach (var (key, value) in db.GetValuesFrom(from, to))
                {
                    id.TryWriteBytes(key);
                    db.Put(key, value);
                }
        }

        private async IAsyncEnumerable<List<(byte[], JournalRecordEntity)>> GetJournalValuesPage(List<IJournalValuesDatabase> databases, int count, GetJournalValuesFunc getValues)
        {
            var result = new List<(byte[], JournalRecordEntity)>(SensorValuesPageCount);
            var totalCount = 0;

            foreach (var database in databases)
            {
                foreach (var value in getValues(database))
                {
                    result.Add((value.Item1, JsonSerializer.Deserialize<JournalRecordEntity>(value.Item2)));
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
            _logger.Info("Starting disposing DatabaseCode...");
            _environmentDatabase.Dispose();
            using (var enumerator = _sensorValuesDatabases.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var item = enumerator.Current;

                    item?.Dispose();
                }
            }
            
            using (var enumerator = _journalValuesDatabases.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var item = enumerator.Current;

                    item?.Dispose();
                }
            }
            
            _logger.Info("DatabaseCore dispposed");
        }
    }
}