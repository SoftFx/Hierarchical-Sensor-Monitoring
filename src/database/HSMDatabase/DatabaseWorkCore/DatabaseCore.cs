using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using NLog;
using HSMCommon.Constants;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMDatabase.Extensions;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.Settings;
using HSMDatabase.SnapshotsDb;
using HSMServer.Core.DataLayer;
using HSMDatabase.AccessManager.Formatters;


namespace HSMDatabase.DatabaseWorkCore
{
    public sealed class DatabaseCore : IDatabaseCore
    {
        public int SensorValuesPageCount => 100;

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly MemoryPackFormatter _formatter = new MemoryPackFormatter();

        private readonly SensorValuesDatabaseDictionary _sensorValuesDatabases;
        private readonly JournalValuesDatabaseDictionary _journalValuesDatabases;
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly IDatabaseSettings _settings;

        public IDashboardCollection Dashboards { get; }

        public IChangeSetCollection ChangeSets { get; }

        public ISnapshotDatabase Snapshots { get; }

        // Compaction state (#1801), changed with CompareExchange only. Idle to Busy while a
        // compaction (the manual whole-database one or a background shard step) runs, so
        // two callers never both enter; Idle to Closed once, by Dispose, after which no
        // compaction starts. Dispose waits for Busy to end: a LevelDB compaction cannot be
        // cancelled, and closing a shard under it would free the native database that the
        // running compaction still uses.
        private const int CompactIdle = 0, CompactBusy = 1, CompactClosed = 2;
        private static readonly TimeSpan ShutdownWaitLogInterval = TimeSpan.FromSeconds(30);

        private int _compactState;
        private int _disposeStarted;
        private volatile string _compactingDatabase;

        // What holds the compaction state (set when it is taken): a pass refused as Busy names it (#1805).
        private volatile string _compactOwner;

        // Shards whose background compaction failed, and when they may be tried again.
        // Touched only while the compaction state is Busy.
        private readonly Dictionary<string, DateTime> _compactionRetryAfter = [];

        public bool IsCompactRunning => Volatile.Read(ref _compactState) == CompactBusy;

        internal bool IsDisposeStarted => Volatile.Read(ref _disposeStarted) == 1;

        // Test seam: wraps every compaction of a weekly shard (the shard, then the call
        // that compacts it), so a test can hold a compaction open or make it throw.
        internal Action<IIntervalDatabase, Action> CompactShardHook { get; set; }

        public bool IsExportRunning { get; private set; }

        public long TotalDbSize => _settings.DatabaseFolder.GetSize();

        public long ConfigDbSize => EnvironmentDbSize + ServerLayoutDbSize + Snapshots.Size;

        public long EnvironmentDbSize => _settings.PathToEnvironmentDb.GetSize();

        public long ServerLayoutDbSize => _settings.PathToServerLayoutDb.GetSize();

        public long SensorHistoryDbSize
        {
            get
            {
                long size = 0;

                // A sealed week (#1803) adds its chunk database; an incomplete one takes disk space too.
                foreach (var db in _sensorValuesDatabases)
                    size += db.Name.GetSize() + _settings.GetPathToSealedSensorValueDatabase(db.From, db.To).GetSize();

                return size;
            }
        }

        public long JournalDbSize => _settings.PathToJournalDb.GetSize();

        public long BackupsSize => _settings.DatabaseBackupsFolder.GetSize();

        public List<ISensorValuesDatabase> SensorValuesDatabases => [.. _sensorValuesDatabases.OrderBy(x => x.From)];

        public IDatabaseSettings DatabaseSettings => _settings;

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
            ChangeSets = new ChangeSetCollection(_settings.PathToChangeSetsDb);
            Snapshots = new SnapshotsDatabase(_settings.PathToSnaphotsDb);

            _logger.Info($"{nameof(DatabaseCore)} initialized");
        }


        public TaskResult<string> BackupEnvironment(string backupPath) => _environmentDatabase.Backup(backupPath);


        #region Fill Sensors (start app)


        public byte[] GetLatestValue(Guid sensorId, long to)
        {
            var maxKey = new DbKey(sensorId, to);

            foreach (var database in _sensorValuesDatabases)
                if (database.From <= to)
                {
                    var value = database.GetLatest(maxKey.ToBytes(), maxKey.ToPrefixBytes());

                    if (value is not null)
                        return value;
                }

            return null;
        }

        public byte[] GetFirstValue(Guid sensorId)
        {
            var minKey = new DbKey(sensorId, DateTime.MinValue);

            foreach (var database in _sensorValuesDatabases.Reverse())
            {
                var value = database.GetFirst(minKey.ToBytes(), minKey.ToPrefixBytes());

                if (value is not null)
                    return value;
            }

            return null;
        }


        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds)
        {
            var results = new Dictionary<Guid, (byte[], byte[])>();

            foreach (var db in _sensorValuesDatabases.Reverse())
            {
                _logger.Info($"Start reading {db.Name}");
                results = db.GetLastAndFirstValues(sensorIds, results);
                _logger.Info($"Stop reading {db.Name}");
            }

            return results;
        }


        //public Dictionary<Guid, byte[]> GetLatestValues(Dictionary<Guid, long> sensors)
        //{
        //    var orderedList = sensors.OrderByDescending(u => u.Value).ToList();
        //    var result = GetResult(sensors.Keys.ToList());

        //    var curDb = _sensorValuesDatabases.GetEnumerator();
        //    var maxBorder = DateTime.MaxValue.Ticks;

        //    var dbExist = curDb.MoveNext(); //go to first db

        //    foreach (var (sensorId, time) in orderedList)
        //        if (time < maxBorder) //skip no data sensors
        //        {
        //            while (dbExist && !curDb.Current.IsInclude(time))
        //                dbExist = curDb.MoveNext();

        //            if (dbExist)
        //            {
        //                var id = sensorId.ToString();

        //                result[sensorId] = curDb.Current.Get(BuildSensorValueKey(id, time), Encoding.UTF8.GetBytes(id));
        //            }
        //            else
        //                break;
        //        }

        //    curDb.Dispose();

        //    return result;
        //}

        public Dictionary<Guid, byte[]> GetLatestValuesFromTo(Dictionary<Guid, (long, long)> sensors)
        {
            var result = GetResult(sensors.Keys.ToList());

            var tempResult = new Dictionary<byte[], (long from, byte[] to, byte[] value)>(sensors.Count);

            foreach (var (id, (from, to)) in sensors)
            {
                var key = new DbKey(id, to);
                tempResult.Add(key.ToPrefixBytes(), (from, key.ToBytes(), null));
            }

            foreach (var database in _sensorValuesDatabases)
                database.FillLatestValues(tempResult);

            foreach (var (key, (_, _, value)) in tempResult)
                result[new Guid(key)] = value;

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

        // The same writes as the two methods above, with their outcome (#1756): null when
        // written, otherwise the failure. A sensor whose id was listed and whose row was
        // then not written is taken off the list again: its caller treats it as not
        // created, and an id without a row is only skipped when the sensors are loaded.
        public Exception AddSensorChecked(SensorEntity entity)
        {
            if (_environmentDatabase.WriteSensorIdToList(entity.Id) is { } listFailure)
                return listFailure;

            var rowFailure = _environmentDatabase.WriteSensor(entity);

            if (rowFailure is not null)
                _environmentDatabase.RemoveSensorIdFromList(entity.Id);

            return rowFailure;
        }

        public Exception UpdateSensorChecked(SensorEntity entity) =>
            _environmentDatabase.WriteSensor(entity);

        public void ClearSensorValues(Guid sensorId, DateTime from, DateTime to)
        {
            var fromTicks = from.ToUniversalTime().Ticks;
            var toTicks = to.ToUniversalTime().Ticks;

            var fromBytes = new DbKey(sensorId, fromTicks).ToBytes();
            var toBytes = new DbKey(sensorId, toTicks).ToBytes();

            foreach (var db in _sensorValuesDatabases)
            {
                if (db.Overlaps(fromTicks, toTicks))
                    db.RemoveSensorValues(fromBytes, toBytes);
                else if (db.To < fromTicks)
                    break;
            }
        }

        // Sensor-move merge (#1720): the one place a history row changes its
        // sensor id. A row's partition is chosen by its tick, so the copy of a
        // row lands in the same partition as the row itself. Existing rows of
        // the target win (ADR-0018: the moved sensor's row wins a same-tick
        // collision). Both key ranges are walked in step (both descending), so
        // memory stays constant whatever the history size; the iterators see
        // the partition as it was when they opened, so the copies written
        // meanwhile are never read back. Not atomic: a re-run after a crash
        // counts the rows the first run copied as collisions. A failed write
        // throws: the merge then keeps the source sensor.
        public (long Copied, long Collisions) MergeSensorValues(Guid fromSensorId, Guid intoSensorId)
        {
            var fromStart = new DbKey(fromSensorId, DateTime.MinValue.Ticks).ToBytes();
            var fromEnd = new DbKey(fromSensorId, DateTime.MaxValue.Ticks).ToBytes();
            var intoStart = new DbKey(intoSensorId, DateTime.MinValue.Ticks).ToBytes();
            var intoEnd = new DbKey(intoSensorId, DateTime.MaxValue.Ticks).ToBytes();

            long copied = 0, collisions = 0;

            foreach (var db in _sensorValuesDatabases)
            {
                IEnumerator<long> into = null;
                var hasInto = false;

                try
                {
                    foreach (var (keyBytes, valueBytes) in db.GetKeysValuesTo(fromStart, fromEnd))
                    {
                        // Opened on the first source row: most partitions hold none.
                        if (into is null)
                        {
                            into = db.GetKeysValuesTo(intoStart, intoEnd)
                                .Select(kv => DbKey.FromBytes(kv.key).Timestamp)
                                .GetEnumerator();
                            hasInto = into.MoveNext();
                        }

                        var tick = DbKey.FromBytes(keyBytes).Timestamp;

                        while (hasInto && into.Current > tick)
                            hasInto = into.MoveNext();

                        if (hasInto && into.Current == tick)
                        {
                            collisions++;
                            continue;
                        }

                        // Throws on failure: the caller deletes the source next.
                        db.PutSensorValueOrThrow(new DbKey(intoSensorId, tick).ToBytes(), valueBytes);
                        copied++;
                    }
                }
                finally
                {
                    into?.Dispose();
                }
            }

            return (copied, collisions);
        }

        public (long dateCnt, long keySize, long valueSize) CalculateSensorHistorySize(Guid sensorId)
        {
            var fromKey = new DbKey(sensorId, DateTime.MinValue.Ticks).ToBytes();
            var toKey = new DbKey(sensorId, DateTime.MaxValue.Ticks).ToBytes();

            var totalDataCnt = 0L;
            var totalKeySize = 0L;
            var totalValueSize = 0L;

            // A sealed week (#1803) counts the values a read returns and the bytes of its overlay rows and chunks.
            foreach (var db in _sensorValuesDatabases)
            {
                var (count, keySize, valueSize) = db.CalculateHistorySize(fromKey, toKey);

                totalDataCnt += count;
                totalKeySize += keySize;
                totalValueSize += valueSize;
            }

            return (totalDataCnt, totalKeySize, totalValueSize);
        }

        public void RemoveSensorWithMetadata(Guid sensorId)
        {
            _environmentDatabase.RemoveSensor(sensorId.ToString());
            _environmentDatabase.RemoveSensorIdFromList(sensorId.ToString());

            ClearSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue);
        }

        public void AddSensorValue(Guid sensorId, BaseValue value)
        {
            var dbs = _sensorValuesDatabases.GetDatabaseByTime(value.Time.Ticks);

            var key = new DbKey(sensorId, value.Time.Ticks);
            var data = _formatter.Serialize(value);

            dbs.PutSensorValue(key.ToBytes(), data);
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

            var fromBytes = new DbKey(sensorId, fromTicks).ToBytes();
            var toBytes = new DbKey(sensorId, toTicks).ToBytes();

            var databases = _sensorValuesDatabases.Where(db => db.Overlaps(fromTicks, toTicks));
            GetValuesFunc getValues = (db) => db.GetValuesTo(fromBytes, toBytes);

            if (count > 0)
            {
                databases.Reverse();
                getValues = (db) => db.GetValuesFrom(fromBytes, toBytes);
            }

            return GetSensorValuesPage(databases, count, getValues);
        }

        private async IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(IEnumerable<ISensorValuesDatabase> databases, int count, GetValuesFunc getValues)
        {
            var result = new List<byte[]>(SensorValuesPageCount);
            var totalCount = 0;
            var requestedCount = Math.Abs(count);

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

                    if (requestedCount == totalCount)
                    {
                        yield return result;
                        yield break;
                    }
                }
            }

            yield return result;
        }

        public async IAsyncEnumerable<byte[]> GetSensorValues(Guid sensorId, DateTime from, DateTime to)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var fromBytes = new DbKey(sensorId, fromTicks).ToBytes();
            var toBytes = new DbKey(sensorId, toTicks).ToBytes();


            //if (count > 0)
            //    count *= (-1);

            //var currentCount = 0;
            //var requestedCount = Math.Abs(count);


            foreach (var database in _sensorValuesDatabases.Where(db => db.Overlaps(fromTicks, toTicks)))
            {
                foreach (byte[] bytes in database.GetValuesTo(fromBytes, toBytes))
                {
                    //if (requestedCount == currentCount)
                    //    yield break;

                    //currentCount++;

                    yield return bytes;
                }
            }

        }

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

        public PolicyEntity GetPolicy(Guid id) => _environmentDatabase.GetPolicy(id.ToByteArray());

        public bool TryGetPolicy(Guid id, out PolicyEntity entity) => _environmentDatabase.TryGetPolicy(id.ToByteArray(), out entity);

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

        #region Api tokens

        public bool TryInsertApiToken(ApiTokenEntity entity) => _environmentDatabase.TryInsertApiToken(entity);

        public bool TryRotateApiToken(ApiTokenEntity revokedOld, ApiTokenEntity replacement) =>
            _environmentDatabase.TryRotateApiToken(revokedOld, replacement);

        public void PutApiToken(ApiTokenEntity entity) => _environmentDatabase.PutApiToken(entity);

        public ApiTokenEntity GetApiToken(string tokenId) => _environmentDatabase.GetApiToken(tokenId);

        public bool RemoveApiToken(string tokenId) => _environmentDatabase.RemoveApiToken(tokenId);

        public List<(string KeyTokenId, ApiTokenEntity Entity)> GetAllApiTokens() => _environmentDatabase.ReadAllApiTokens();

        public long GetGlobalRevocationGeneration() => _environmentDatabase.GetGlobalRevocationGeneration();

        public long AdvanceGlobalRevocationGeneration() => _environmentDatabase.AdvanceGlobalRevocationGeneration();

        public long GetOwnerRevocationGeneration(Guid ownerUserId) =>
            _environmentDatabase.GetOwnerRevocationGeneration(ownerUserId);

        public long AdvanceOwnerRevocationGeneration(Guid ownerUserId) =>
            _environmentDatabase.AdvanceOwnerRevocationGeneration(ownerUserId);

        public void PutApiTokenSecurityEvent(ApiTokenSecurityEventEntity entity) =>
            _environmentDatabase.PutApiTokenSecurityEvent(entity);

        public List<ApiTokenSecurityEventEntity> ReadApiTokenSecurityEvents() =>
            _environmentDatabase.ReadApiTokenSecurityEvents();

        public int RemoveApiTokenSecurityEventsBefore(long ticksCutoffUtc, int limit) =>
            _environmentDatabase.RemoveApiTokenSecurityEventsBefore(ticksCutoffUtc, limit);

        #endregion

        #region Environment database : User

        public void AddUser(UserEntity entity) => _environmentDatabase.AddUser(entity);

        public void RemoveUser(UserEntity entity) => _environmentDatabase.RemoveUser(entity);

        public void UpdateUser(UserEntity entity) => AddUser(entity);

        public List<UserEntity> GetUsers() => _environmentDatabase.ReadUsers().ToList();

        public List<UserEntity> GetUsersPage(int page, int pageSize) => _environmentDatabase.ReadUsersPage(page, pageSize).ToList();

        #endregion

        #region Environment database : Telegram chat

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

        #region Environment database : Slack destination

        public SlackDestinationEntity GetSlackDestination(byte[] id) => _environmentDatabase.GetSlackDestination(id);

        public List<SlackDestinationEntity> GetSlackDestinations()
        {
            var destinations = new List<SlackDestinationEntity>(1 << 4);
            var ids = _environmentDatabase.GetSlackDestinationsList();

            foreach (var id in ids)
            {
                var entity = _environmentDatabase.GetSlackDestination(id);
                if (entity != null)
                    destinations.Add(entity);
            }

            return destinations;
        }

        #endregion

        #region Environment database : Chat

        public void AddChat(ChatEntity chat)
        {
            _environmentDatabase.AddChatToList(chat.Id);
            _environmentDatabase.AddChat(chat);
        }

        public void UpdateChat(ChatEntity chat) => _environmentDatabase.AddChat(chat);

        public void RemoveChat(byte[] chatId)
        {
            _environmentDatabase.RemoveChat(chatId);
            _environmentDatabase.RemoveChatFromList(chatId);
        }

        public ChatEntity GetChat(byte[] chatId) => _environmentDatabase.GetChat(chatId);

        public List<ChatEntity> GetChats()
        {
            var chats = new List<ChatEntity>(1 << 4);
            var ids = _environmentDatabase.GetChatsList();

            foreach (var id in ids)
            {
                var entity = _environmentDatabase.GetChat(id);
                if (entity != null)
                    chats.Add(entity);
            }

            return chats;
        }

        #endregion

        #region Journal

        public void AddJournalValue(JournalKey journalKey, JournalRecordEntity value)
        {
            var dbs = _journalValuesDatabases.GetDatabaseByTime(journalKey.Time);

            dbs.Put(journalKey.GetBytes(), value);
        }

        public void RemoveJournalValues(Guid id, Guid parentId)
        {
            var fromTicks = DateTime.MinValue.Ticks;
            var toTicks = DateTime.MaxValue.Ticks;

            var fromBytes = new JournalKey(id, fromTicks, RecordType.Actions).GetBytes();
            var toBytes = new JournalKey(id, toTicks, RecordType.Changes).GetBytes();

            foreach (var db in _journalValuesDatabases)
                if (db.Overlaps(fromTicks, toTicks))
                {
                    PutRecordsToParent(fromBytes, toBytes, parentId, db);

                    db.Remove(fromBytes, toBytes);
                }
        }

        // The global Journal read (#1588): every CHANGES-type record of the
        // range across all nodes. The key layout is guid|type|ticks puts the
        // GUID first, so NO byte range isolates a time window or even the
        // Changes type — the range below spans a whole shard and the record
        // type and time are refined in memory. The shard bounds (Overlaps)
        // and the callers' bounded window keep the walk bounded; the cost is
        // why the journal service clamps unbounded caller ranges.
        public List<(byte[] Key, JournalRecordEntity Entity)> GetAllJournalValues(DateTime from, DateTime to)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var maxId = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

            var fromBytes = new JournalKey(Guid.Empty, DateTime.MinValue.Ticks, RecordType.Changes).GetBytes();
            var toBytes = new JournalKey(maxId, DateTime.MaxValue.Ticks, RecordType.Changes).GetBytes();

            var result = new List<(byte[], JournalRecordEntity)>();

            foreach (var db in _journalValuesDatabases)
                if (db.Overlaps(fromTicks, toTicks))
                    foreach (var (key, value) in db.GetValuesFrom(fromBytes, toBytes))
                    {
                        var journalKey = JournalKey.FromBytes(key);

                        if (journalKey.Type != RecordType.Changes ||
                            journalKey.Time < fromTicks || journalKey.Time > toTicks)
                            continue;

                        // One corrupt row must not 500 the whole list
                        // (#1588 review round 4): skip it, loudly.
                        try
                        {
                            result.Add((key, JsonSerializer.Deserialize<JournalRecordEntity>(value)));
                        }
                        catch (Exception e)
                        {
                            _logger.Error(e,
                                $"Corrupt journal row skipped for {journalKey.Id} at {journalKey.Time}");
                        }
                    }

            return result;
        }

        public bool TryGetJournalValue(JournalKey key, out JournalRecordEntity entity)
        {
            entity = null;

            try
            {
                // READ-ONLY shard lookup (#1588 review round 2):
                // GetDatabaseByTime CREATES the week's shard — a caller-
                // crafted record id must not mint empty shards (and open
                // handles) on the read path. Only an EXISTING overlapping
                // shard can hold the key; everything else answers false.
                var db = _journalValuesDatabases.FirstOrDefault(db => db.Overlaps(key.Time, key.Time));

                return db is not null && db.TryGet(key.GetBytes(), out var raw)
                    && (entity = JsonSerializer.Deserialize<JournalRecordEntity>(raw)) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public IAsyncEnumerable<List<(byte[] Key, JournalRecordEntity Entity)>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType types, int count)
        {
            var fromTicks = from.Ticks;
            var toTicks = to.Ticks;

            var newestFirst = count < 0;

            // Inside a shard every record type is its own key range
            // (sensorId|type|ticks), so the per-type streams are merged by time:
            // a count-limited page must keep the records nearest its starting
            // edge whatever their type.
            IEnumerable<(byte[], byte[])> GetValuesEnumerator(Func<byte[], byte[], IEnumerable<(byte[], byte[])>> requestDb)
            {
                var streams = new List<IEnumerable<(byte[], byte[])>>(2);

                // Actions is 0, so HasFlag(Actions) is always true: every read
                // includes Actions records (pinned by JournalBorderTests).
                foreach (var recordType in Enum.GetValues<RecordType>())
                    if (types.HasFlag(recordType))
                    {
                        var fromBytes = new JournalKey(sensorId, fromTicks, recordType).GetBytes();
                        var toBytes = new JournalKey(sensorId, toTicks, recordType).GetBytes();

                        streams.Add(requestDb(fromBytes, toBytes));
                    }

                return MergeJournalStreamsByTime(streams, newestFirst);
            }

            // The shard dictionary enumerates newest week first.
            var databases = _journalValuesDatabases.Where(db => db.Overlaps(fromTicks, toTicks));
            GetJournalValuesFunc getValues;

            if (newestFirst)
                getValues = (db) => GetValuesEnumerator(db.GetValuesTo);
            else
            {
                databases = databases.Reverse();
                getValues = (db) => GetValuesEnumerator(db.GetValuesFrom);
            }

            return GetJournalValuesPage(databases, count, getValues);
        }

        private static IEnumerable<(byte[], byte[])> MergeJournalStreamsByTime(List<IEnumerable<(byte[], byte[])>> streams, bool newestFirst)
        {
            var enumerators = new List<IEnumerator<(byte[], byte[])>>(streams.Count);
            var heads = new List<long>(streams.Count); // ticks of each enumerator's Current

            try
            {
                foreach (var stream in streams)
                {
                    // Listed before MoveNext so the finally disposes it if MoveNext throws.
                    var enumerator = stream.GetEnumerator();
                    enumerators.Add(enumerator);

                    if (enumerator.MoveNext())
                        heads.Add(JournalKey.GetTime(enumerator.Current.Item1));
                    else
                    {
                        enumerator.Dispose();
                        enumerators.RemoveAt(enumerators.Count - 1);
                    }
                }

                while (enumerators.Count > 0)
                {
                    var next = 0;

                    // Equal ticks: the earlier stream wins oldest first and
                    // the later one newest first, so the two directions
                    // are exact mirrors of each other.
                    for (var i = 1; i < enumerators.Count; i++)
                        if (newestFirst ? heads[i] >= heads[next] : heads[i] < heads[next])
                            next = i;

                    yield return enumerators[next].Current;

                    if (enumerators[next].MoveNext())
                        heads[next] = JournalKey.GetTime(enumerators[next].Current.Item1);
                    else
                    {
                        enumerators[next].Dispose();
                        enumerators.RemoveAt(next);
                        heads.RemoveAt(next);
                    }
                }
            }
            finally
            {
                foreach (var enumerator in enumerators)
                    enumerator.Dispose();
            }
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

        private async IAsyncEnumerable<List<(byte[], JournalRecordEntity)>> GetJournalValuesPage(IEnumerable<IJournalValuesDatabase> databases, int count, GetJournalValuesFunc getValues)
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

        #region Alert template

        public void AddAlertTemplate(AlertTemplateEntity entity)
        {
            _environmentDatabase.AddAlertTemplateIdToList(entity.Id);
            _environmentDatabase.AddAlertTemplate(entity);
        }

        public void UpdateAlertTemplate(AlertTemplateEntity entity) => _environmentDatabase.UpdateAlertTemplate(entity);

        public void RemoveAlertTemplate(Guid id) => _environmentDatabase.RemoveAlertTemplate(id.ToByteArray());

        public List<AlertTemplateEntity> GetAllAlertTemplates()
        {
            var ids = _environmentDatabase.GetAllAlertTemplatesIds();

            var result = new List<AlertTemplateEntity>(ids.Count);

            foreach (var id in ids)
            {
                var template = _environmentDatabase.GetAlertTemplate(id);

                if (template != null)
                    result.Add(template);
            }

            return result;
        }

        #endregion


        #region AlertSchedule
        public List<AlertScheduleEntity> GetAllAlertSchedules()
        {
            var ids = _environmentDatabase.GetAllAlertScheduleIds();

            var result = new List<AlertScheduleEntity>(ids.Count);

            foreach (var id in ids)
            {
                var schedule = _environmentDatabase.GetAlertSchedule(id);

                if (schedule != null)
                    result.Add(schedule);
            }

            return result;
        }

        public AlertScheduleEntity GetAlertSchedule(Guid id)
        {
            return _environmentDatabase.GetAlertSchedule(id.ToByteArray());
        }

        public void AddAlertSchedule(AlertScheduleEntity schedule)
        {
            _environmentDatabase.AddAlertSchedule(schedule);
            _environmentDatabase.AddAlertScheduleIdToList(schedule.Id);
        }

        public void RemoveAlertSchedule(Guid id)
        {
            _environmentDatabase.RemoveAlertSchedule(id.ToByteArray());
        }
        #endregion
        public void Compact()
        {
            if (!TryEnterCompact("the manual database compaction"))
            {
                if (IsDisposeStarted)
                    _logger.Info("CompactDB refused: the database is shutting down");

                return;
            }

            try
            {
                _logger.Info($"CompactDB start: Enviroment database {ConfigDbSize}");
                try
                {
                    _compactingDatabase = "Enviroment";
                    _environmentDatabase.Compact();
                }
                catch (Exception ex)
                {
                    _logger.Error($"An error was occurred while compacting database [Enviroment]: {ex.Message}", ex);
                }
                _logger.Info($"CompactDB stop: Enviroment database {ConfigDbSize}");

                var currentWeekStart = DateTimeMethods.GetStartOfWeekTicks(DateTime.UtcNow.Ticks);

                _logger.Info($"CompactDB start: Sensor values database {SensorHistoryDbSize}");

                foreach (var shard in _sensorValuesDatabases.GetShards())
                    if (!StopForShutdown())
                        CompactAndMarkClosed(shard.Path, () => shard.Database, currentWeekStart);

                _logger.Info($"CompactDB stop: Sensor values database {SensorHistoryDbSize}");

                _logger.Info($"CompactDB start: Journal database {JournalDbSize}");

                foreach (var shard in _journalValuesDatabases.GetShards())
                    if (!StopForShutdown())
                        CompactAndMarkClosed(shard.Path, () => shard.Database, currentWeekStart);

                _logger.Info($"CompactDB stop: Journal database {JournalDbSize}");
            }
            catch (Exception ex)
            {
                _logger.Error($"An error was occurred while compacting databases: {ex.Message}", ex);
            }
            finally
            {
                ExitCompact();
            }

        }

        // The manual path compacts every shard as before, and marks the closed ones so the
        // background step does not compact them again (#1801). The current week is not
        // marked: its next write would remove the marker anyway. A shard that fails (to
        // open or to compact) is logged with its name and the next shard goes on.
        private void CompactAndMarkClosed(string name, Func<IIntervalDatabase> open, long currentWeekStart)
        {
            try
            {
                var db = open();
                var changeCount = db.ChangeCount;
                var sizeBefore = ShardSize(name, db);

                CompactShard(db);

                if (db.From < currentWeekStart)
                    TryMarkCompacted(db, changeCount, sizeBefore, ShardSize(name, db));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error was occurred while compacting database [{name}]: {ex.Message}");
            }
        }

        // The manual run goes over every database and can take minutes: once disposal has
        // begun it compacts no further shard, so the shutdown waits for one shard, not all.
        private bool StopForShutdown()
        {
            if (!IsDisposeStarted)
                return false;

            if (_compactingDatabase is not null)
            {
                _logger.Info("CompactDB stopped: the database is shutting down; the remaining shards are left as they are");
                _compactingDatabase = null;
            }

            return true;
        }

        // A sensor history week counts its chunk directory too: Compact() of a sealed week compacts both (#1803).
        private long ShardSize(string name, IIntervalDatabase db) =>
            name.GetSize() + (db is ISensorValuesDatabase ? _settings.GetPathToSealedSensorValueDatabase(db.From, db.To).GetSize() : 0);

        private void CompactShard(IIntervalDatabase db)
        {
            _compactingDatabase = db.Name;

            if (CompactShardHook is { } hook)
                hook(db, db.Compact);
            else
                db.Compact();
        }

        // A marker the manual path cannot write costs one more compaction later, and must
        // not stop the manual compaction of the remaining shards.
        private void TryMarkCompacted(IIntervalDatabase db, long changeCount, long sizeBefore, long sizeAfter)
        {
            try
            {
                db.TryMarkCompacted(changeCount, CompactedMarkerContent(sizeBefore, sizeAfter));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to write the compaction marker of {db.Name}; the background compaction will compact it again");
            }
        }

        private static string CompactedMarkerContent(long sizeBefore, long sizeAfter) =>
            $"Compacted at {DateTime.UtcNow:O}: {sizeBefore} -> {sizeAfter} bytes{Environment.NewLine}";

        private bool TryEnterCompact(string owner)
        {
            if (IsDisposeStarted || Interlocked.CompareExchange(ref _compactState, CompactBusy, CompactIdle) != CompactIdle)
                return false;

            // Dispose may have begun between the check and the swap: this compaction does
            // not start either, and Dispose (waiting for the state) goes on.
            if (IsDisposeStarted)
            {
                ExitCompact();
                return false;
            }

            _compactOwner = owner;

            return true;
        }

        // Why the compaction state was busy, for a pass refused because of it: what holds it and, when known, the
        // database it works on. Read without a lock: the holder may finish meanwhile, and the text is only for the log.
        private string BusyReason()
        {
            var owner = _compactOwner;
            var database = _compactingDatabase;

            if (owner is null)
                return "another compaction or sealing step was running";

            return database is null ? $"{owner} was running" : $"{owner} was running ({database})";
        }

        private void ExitCompact()
        {
            _compactOwner = null;
            _compactingDatabase = null;
            Interlocked.CompareExchange(ref _compactState, CompactIdle, CompactBusy);
        }

        // Called by Dispose before any database is closed: refuses every later compaction
        // and waits for a running one to finish, however long it takes, since it cannot be
        // cancelled. The log says what the shutdown is waiting for.
        private void CloseCompaction()
        {
            System.Diagnostics.Stopwatch watch = null;
            var nextLog = TimeSpan.Zero;

            while (Interlocked.CompareExchange(ref _compactState, CompactClosed, CompactIdle) != CompactIdle)
            {
                watch ??= System.Diagnostics.Stopwatch.StartNew();

                if (watch.Elapsed >= nextLog)
                {
                    _logger.Warn($"Shutdown is waiting for the compaction of [{_compactingDatabase ?? "unknown"}] to finish ({watch.Elapsed.TotalSeconds:F0} s so far)");
                    nextLog += ShutdownWaitLogInterval;
                }

                Thread.Sleep(50);
            }

            if (watch is not null)
                _logger.Info($"The compaction finished; shutdown goes on after waiting {watch.Elapsed.TotalSeconds:F1} s");
        }

        public HistoryShardCompactionOutcome CompactNextDueHistoryShard(DateTime utcNow, TimeSpan quietPeriod)
        {
            if (!TryEnterCompact("the history shard compaction step"))
                return IsDisposeStarted ? HistoryShardCompactionOutcome.ShuttingDown : HistoryShardCompactionOutcome.Busy;

            try
            {
                var shards = new Dictionary<string, Func<IIntervalDatabase>>();
                var candidates = new List<HistoryShardCandidate>();

                // Sensor history first: on the same week the planner takes the first listed.
                AddCompactionCandidates(_sensorValuesDatabases, shards, candidates);
                AddCompactionCandidates(_journalValuesDatabases, shards, candidates);

                if (HistoryShardCompactionPlanner.SelectNext(candidates, utcNow, quietPeriod) is not { } next)
                    return HistoryShardCompactionOutcome.NothingDue;

                return CompactHistoryShard(next.Name, shards[next.Name], utcNow);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "History shard compaction step failed");
                return HistoryShardCompactionOutcome.Failed;
            }
            finally
            {
                ExitCompact();
            }
        }

        // A shard not opened yet has not changed since the server started (every write
        // opens it): its marker file is all there is to know, and reading it does not
        // open the LevelDB database.
        private void AddCompactionCandidates<T>(ValuesDatabaseDictionary<T> dictionary,
            Dictionary<string, Func<IIntervalDatabase>> shards, List<HistoryShardCandidate> candidates)
            where T : IIntervalDatabase
        {
            foreach (var shard in dictionary.GetShards())
            {
                if (!shards.TryAdd(shard.Path, () => shard.Database))
                    continue;

                _compactionRetryAfter.TryGetValue(shard.Path, out var retryAfter);
                DateTime? retry = retryAfter == default ? null : retryAfter;

                candidates.Add(shard.IsOpened
                    ? new HistoryShardCandidate(shard.Path, shard.From, shard.Database.IsCompacted, shard.Database.LastChangeUtc, retry)
                    : new HistoryShardCandidate(shard.Path, shard.From, CompactedShardMarker.ExistsIn(shard.Path), null, retry));
            }
        }

        private HistoryShardCompactionOutcome CompactHistoryShard(string name, Func<IIntervalDatabase> open, DateTime utcNow)
        {
            try
            {
                var db = open();
                var sizeBefore = ShardSize(name, db);
                var changeCount = db.ChangeCount;
                var watch = System.Diagnostics.Stopwatch.StartNew();

                CompactShard(db);

                var sizeAfter = ShardSize(name, db);
                // A marker that cannot be written throws into the failure path below: the
                // shard then waits for the backoff instead of being compacted every pass.
                var marked = db.TryMarkCompacted(changeCount, CompactedMarkerContent(sizeBefore, sizeAfter));

                _compactionRetryAfter.Remove(name);

                _logger.Info($"History shard compacted: {name}: {sizeBefore} -> {sizeAfter} bytes in {watch.Elapsed.TotalSeconds:F1} s" +
                    (marked ? string.Empty : "; it changed meanwhile and stays due"));

                return HistoryShardCompactionOutcome.Compacted;
            }
            catch (Exception ex)
            {
                var retryAfter = utcNow + HistoryShardCompactionPlanner.FailureBackoff;
                _compactionRetryAfter[name] = retryAfter;

                _logger.Error(ex, $"History shard compaction failed: {name}; next attempt not before {retryAfter:O}");

                return HistoryShardCompactionOutcome.Failed;
            }
        }

        #region Sealing job (#1804)

        // Weeks whose last sealing action failed, and when they may be tried again. Touched only while the
        // compaction state is Busy, like _compactionRetryAfter.
        private readonly Dictionary<string, DateTime> _sealingRetryAfter = [];

        // What the job last counted to fold in a sealed week, keyed by the week. Touched only while the compaction state
        // is Busy.
        private readonly Dictionary<string, FoldObservation> _foldObservations = [];

        // Weeks an operator has to look at: from their marker files (refreshed every pass), and from an action that
        // stopped on them (kept until an action on the week succeeds). Each reason is an error in the log once per process.
        private readonly Dictionary<string, string> _stageAttention = [];
        private readonly Dictionary<string, string> _actionAttention = [];
        private readonly HashSet<string> _attentionLogged = [];

        private long _sealingSequence;
        private volatile HistorySealingPassReport _lastSealingPass;

        /// <summary>
        /// The count of a week's foldable rows is redone at most this often while its change counter keeps moving (an
        /// aggregated sensor rewrites its span row in the week the span began on every equal value, #1804 review S4), and
        /// at once when the instance serving the week changed or nothing was counted yet.
        /// </summary>
        internal static TimeSpan FoldRecountInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>How long the job waits before it tries again to delete a chunk directory a read still uses.</summary>
        internal static readonly TimeSpan LeftoverRetry = TimeSpan.FromHours(1);

        // UnchangedSinceUtc: since when the number of foldable rows has been the same (a value rewritten in place does not
        // change it), by the job's clock.
        private sealed record FoldObservation(object Instance, long ChangeCount, long FoldableRows, List<Guid> Sensors, DateTime UnchangedSinceUtc, DateTime CountedAtUtc);

        public HistorySealingPassReport LastHistorySealingPass => _lastSealingPass;

        /// <summary>How many times the job counted the foldable rows of a week (tests: the count must not run every pass).</summary>
        internal int FoldCounts { get; private set; }

        // Test seam: runs at each SealingCheckpoints point of the job; a test throws from it to stand for a crash.
        internal Action<string> SealingCheckpoint { get; set; }

        // Test seam: see WeekSealingJob.ChunksBeforeWrite.
        internal Func<Guid, List<byte[]>, List<byte[]>> SealingChunksBeforeWrite { get; set; }


        public HistorySealingPassReport RunHistorySealingPass(DateTime utcNow, HistorySealingSettings settings)
        {
            // Off: nothing is looked at, nothing is created, nothing is reported.
            if (settings is not { Enabled: true })
                return new HistorySealingPassReport { RunUtc = utcNow, Outcome = HistorySealingOutcome.Disabled };

            if (!HistorySealingPlanner.IsWithinWorkingHours(utcNow, settings))
                return Publish(new HistorySealingPassReport { Sequence = NextSealingSequence(), RunUtc = utcNow, Outcome = HistorySealingOutcome.OutsideWorkingHours });

            // A refused pass is not published: the attention lists belong to the step that holds the state, and the
            // self-monitoring keeps showing the last pass that ran. The service logs it with the reason.
            if (!TryEnterCompact("a history sealing pass"))
                return new HistorySealingPassReport
                {
                    RunUtc = utcNow,
                    Outcome = IsDisposeStarted ? HistorySealingOutcome.ShuttingDown : HistorySealingOutcome.Busy,
                    Note = IsDisposeStarted ? null : BusyReason(),
                };

            try
            {
                var weeks = new Dictionary<string, SensorValuesWeek>();
                var candidates = new List<SealingWeekCandidate>();

                _stageAttention.Clear();

                foreach (var shard in _sensorValuesDatabases.GetShards())
                    candidates.Add(SealingCandidate(shard, weeks, utcNow, settings));

                if (HistorySealingPlanner.SelectNext(candidates, utcNow, settings) is not { } step)
                    return Publish(WithAttention(new HistorySealingPassReport
                    {
                        Sequence = NextSealingSequence(),
                        RunUtc = utcNow,
                        Outcome = HistorySealingOutcome.NothingDue,
                        SealedWeeks = CountSealedWeeks(),
                    }));

                var week = weeks.TryGetValue(step.Week.Name, out var opened) ? opened : OpenWeek(step.Week.Name);

                return RunSealingAction(step.Action, week, utcNow, settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "History sealing pass failed");
                return Publish(WithAttention(new HistorySealingPassReport { Sequence = NextSealingSequence(), RunUtc = utcNow, Outcome = HistorySealingOutcome.Failed, Error = ex.Message }));
            }
            finally
            {
                ExitCompact();
            }
        }

        public HistorySealingPassReport UnsealHistoryWeek(string weekName, DateTime utcNow)
        {
            if (!TryEnterCompact($"the unseal of {weekName}"))
                return new HistorySealingPassReport
                {
                    RunUtc = utcNow,
                    Action = HistorySealingAction.Unseal,
                    Week = weekName,
                    Outcome = IsDisposeStarted ? HistorySealingOutcome.ShuttingDown : HistorySealingOutcome.Busy,
                    Note = IsDisposeStarted ? null : BusyReason(),
                };

            try
            {
                var shard = _sensorValuesDatabases.GetShards().FirstOrDefault(s => s.Path == weekName);

                if (shard is null)
                    return new HistorySealingPassReport { RunUtc = utcNow, Action = HistorySealingAction.Unseal, Week = weekName, Outcome = HistorySealingOutcome.NotFound };

                var status = SealedWeekStages.Read(shard.Path, _settings.GetPathToSealedSensorValueDatabase(shard.From, shard.To));

                if (status.Stage is not (SealedWeekStage.SealedKeepingSourceRows or SealedWeekStage.SourceRowsRemoved or SealedWeekStage.Unsealing or SealedWeekStage.UnsealedLeftover))
                    return Publish(WithAttention(new HistorySealingPassReport
                    {
                        Sequence = NextSealingSequence(),
                        RunUtc = utcNow,
                        Action = HistorySealingAction.Unseal,
                        Week = weekName,
                        Outcome = HistorySealingOutcome.NotSealed,
                        SealedWeeks = CountSealedWeeks(),
                        Error = status.Attention,
                    }));

                return RunSealingAction(HistorySealingAction.Unseal, (SensorValuesWeek)shard.Database, utcNow, null);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unseal of {weekName} failed");
                return Publish(WithAttention(new HistorySealingPassReport
                {
                    Sequence = NextSealingSequence(),
                    RunUtc = utcNow,
                    Action = HistorySealingAction.Unseal,
                    Week = weekName,
                    Outcome = HistorySealingOutcome.Failed,
                    Error = ex.Message,
                }));
            }
            finally
            {
                ExitCompact();
            }
        }

        // The state the planner needs, from the marker files; a sealed week whose source rows are gone is opened and its
        // overlay counted, unless a recent count still stands (FoldRecountInterval).
        private SealingWeekCandidate SealingCandidate(ValuesDatabaseDictionary<ISensorValuesDatabase>.Shard shard, Dictionary<string, SensorValuesWeek> weeks,
            DateTime utcNow, HistorySealingSettings settings)
        {
            var to = shard.To;
            var sealedPath = _settings.GetPathToSealedSensorValueDatabase(shard.From, to);

            // A seal whose tail a crash interrupted is finished first (#1804 review U1): file operations only.
            WeekSealingJob.CompleteSealTail(sealedPath, shard.Path);

            var status = SealedWeekStages.Read(shard.Path, sealedPath);

            if (status.Attention is not null)
                NoteAttention(_stageAttention, shard.Path, status.Attention);

            // A week whose deletes are refused (an unreadable delete watermark, #1805 review W2) is reported; its stage is
            // unchanged and the job goes on with it. Only an opened week can have refused a delete.
            if (shard.IsOpened && shard.Database is SensorValuesWeek opened && opened.DeleteRefusal is { } refusal)
                NoteAttention(_stageAttention, shard.Path, refusal);

            _sealingRetryAfter.TryGetValue(shard.Path, out var retryAfter);
            DateTime? retry = retryAfter == default ? null : retryAfter;

            long? foldable = null;
            DateTime? unchangedSince = null;

            if (status.Stage == SealedWeekStage.SourceRowsRemoved && (retry is null || utcNow >= retry) && shard.Database is SensorValuesWeek week && week.IsSealed)
            {
                weeks[shard.Path] = week;

                _foldObservations.TryGetValue(shard.Path, out var observation);

                var sameInstance = observation is not null && ReferenceEquals(observation.Instance, week.ServingInstance);
                var changed = !sameInstance || observation.ChangeCount != week.ChangeCount;

                if (changed && (!sameInstance || utcNow - observation.CountedAtUtc >= FoldRecountInterval || utcNow < observation.CountedAtUtc))
                {
                    var changeCount = week.ChangeCount;

                    FoldCounts++;

                    if (new WeekSealingJob(week, new HistorySealingPassReport(), () => IsDisposeStarted, null).CountFoldableRows() is { } count)
                    {
                        // Rows rewritten in place keep the count, so they do not restart the quiet period: a week whose few
                        // aggregated rows change all the time is folded once per quiet period, not never and not every pass.
                        var since = sameInstance && count.rows == observation.FoldableRows ? observation.UnchangedSinceUtc : utcNow;

                        observation = new FoldObservation(week.ServingInstance, changeCount, count.rows, count.sensors, since, utcNow);
                        _foldObservations[shard.Path] = observation;
                    }
                    else
                        observation = null; // stopped for a shutdown
                }

                foldable = observation?.FoldableRows;
                unchangedSince = observation?.UnchangedSinceUtc;
            }

            return new SealingWeekCandidate(shard.Path, shard.From, to, status, foldable, unchangedSince, retry);
        }

        private SensorValuesWeek OpenWeek(string name) =>
            (SensorValuesWeek)_sensorValuesDatabases.GetShards().First(s => s.Path == name).Database;

        private HistorySealingPassReport RunSealingAction(HistorySealingAction action, SensorValuesWeek week, DateTime utcNow, HistorySealingSettings settings)
        {
            var report = new HistorySealingPassReport { Sequence = NextSealingSequence(), RunUtc = utcNow, Action = action, Week = week.Name };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var job = new WeekSealingJob(week, report, () => IsDisposeStarted, SealingCheckpoint) { ChunksBeforeWrite = SealingChunksBeforeWrite };

            _compactingDatabase = $"{week.Name} ({action})";

            HistorySealingOutcome outcome;

            try
            {
                var completed = action switch
                {
                    HistorySealingAction.Seal => job.Seal(utcNow, utcNow + settings.KeepSourceRows),
                    HistorySealingAction.RemoveSourceRows => job.RemoveSourceRows(utcNow),
                    HistorySealingAction.Fold => job.Fold(_foldObservations[week.Name].Sensors),
                    HistorySealingAction.FinishUnseal or HistorySealingAction.Unseal => job.Unseal(utcNow),
                    HistorySealingAction.DeleteLeftoverChunks => job.DeleteLeftoverChunks(),
                    _ => throw new ArgumentOutOfRangeException(nameof(action)),
                };

                outcome = completed ? HistorySealingOutcome.Done : HistorySealingOutcome.Interrupted;
                _sealingRetryAfter.Remove(week.Name);
                _actionAttention.Remove(week.Name);

                // A leftover directory still in use is checked again later, without holding anything back meanwhile.
                if (!completed && action == HistorySealingAction.DeleteLeftoverChunks)
                    _sealingRetryAfter[week.Name] = utcNow + LeftoverRetry;
            }
            catch (Exception ex)
            {
                var retryAfter = utcNow + HistorySealingPlanner.FailureBackoff;
                _sealingRetryAfter[week.Name] = retryAfter;

                report.Error = ex.Message;
                outcome = HistorySealingOutcome.Failed;

                // Stopped on something an operator has to look at: a chunk directory that may hold the only copy of the week,
                // a chunk that cannot be read.
                if (ex is WeekNeedsAttentionException or SealedChunkReadException)
                    NoteAttention(_actionAttention, week.Name, ex.Message);

                _logger.Error(ex, $"History sealing failed: {action} of {week.Name}; next attempt not before {retryAfter:O}");
            }

            // Recounted on the next pass: the action changed the week, or the instance serving it.
            _foldObservations.Remove(week.Name);

            report.Duration = watch.Elapsed;
            report.SealedWeeks = CountSealedWeeks();
            report.Outcome = outcome;

            WithAttention(report);

            if (outcome == HistorySealingOutcome.Failed)
                _logger.Error($"History sealing: {report} in {report.Duration.TotalSeconds:F1} s");
            else
                _logger.Info($"History sealing: {report} in {report.Duration.TotalSeconds:F1} s" +
                             string.Concat(report.LongestExclusiveHold.Select(h => $"; {h.Key} held the gate up to {h.Value.TotalMilliseconds:F1} ms")));

            return Publish(report);
        }

        private void NoteAttention(Dictionary<string, string> into, string week, string reason)
        {
            into[week] = reason;

            if (_attentionLogged.Add($"{week}|{reason}"))
                _logger.Error($"Sealed history: {week} needs attention: {reason}");
        }

        private HistorySealingPassReport WithAttention(HistorySealingPassReport report)
        {
            foreach (var (week, reason) in _stageAttention.Concat(_actionAttention))
                report.WeeksNeedingAttention.TryAdd(week, reason);

            return report;
        }

        private int CountSealedWeeks()
        {
            var count = 0;

            foreach (var shard in _sensorValuesDatabases.GetShards())
                if (SealedWeekMarker.ExistsIn(_settings.GetPathToSealedSensorValueDatabase(shard.From, shard.To)))
                    count++;

            return count;
        }

        private long NextSealingSequence() => Interlocked.Increment(ref _sealingSequence);

        private HistorySealingPassReport Publish(HistorySealingPassReport report)
        {
            _lastSealingPass = report;
            return report;
        }

        #endregion

        public void ExportValuesDatabase(string name, Dictionary<Guid, string> sensors)
        {
            if (IsExportRunning)
                return;

            IsExportRunning = true;

            try
            {
                var database = _sensorValuesDatabases.FirstOrDefault(x => x.Name == name);

                if (database == null)
                    return;

                var basePath = name.Split(Path.DirectorySeparatorChar);

                if (!Directory.Exists(_settings.ExportFolder))
                    Directory.CreateDirectory(_settings.ExportFolder);

                using var writer = new StreamWriter(Path.Combine(_settings.ExportFolder, $"{basePath[^1]}.csv"));
                foreach (var (keyByte, valueByte) in database.GetAll())
                {
                    var key = DbKey.FromBytes(keyByte);
                    sensors.TryGetValue(key.SensorId, out var path);
                    writer.WriteLine($"{key.SensorId},{path},{new DateTime(key.Timestamp)},{valueByte.Length},{_formatter.Deserialize(valueByte)}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"An error was occurred while compacting databases: {ex.Message}", ex);
            }
            finally
            {
                IsExportRunning = false;
            }
        }

        public IEnumerable<(byte[], byte[])> GetAll()
        {
            foreach (var db in _sensorValuesDatabases)
            {
                foreach (var item in db.GetAll())
                    yield return item;
            }
        }

        public IEnumerable<(byte[], byte[])> MigrateDatabaseV2()
        {
            var settings = new DatabaseSettings() { SensorValuesDatabaseName = "SensorValues" };
            var oldDbs = new SensorValuesDatabaseDictionary(settings, sealedWeeks: false);

            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, settings.DatabaseFolder)))
                yield break;

            foreach (var oldDb in oldDbs)
            {

                foreach (var item in oldDb.GetAll())
                    yield return item;



                var oldDirectory = Path.Combine(Environment.CurrentDirectory, oldDb.Name);
                var newDirectory = Path.Combine(Environment.CurrentDirectory, oldDb.Name.Replace("SensorValues_", "!SensorValues_"));

                try
                {
                    oldDb.Dispose();
                    Directory.Move(oldDirectory, newDirectory);
                }
                catch (Exception ex)
                {
                    _logger.Error($"An error was occured while renaming direcory {oldDirectory} -> {newDirectory}", ex);
                }
            }

        }

        public void Dispose()
        {
            // The server disposes it twice (the tree cache and the container): once is enough.
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return;

            _logger.Info($"Starting disposing {nameof(DatabaseCore)}...");

            CloseCompaction();
            _environmentDatabase.Dispose();
            foreach (var item in _sensorValuesDatabases)
            {
                item?.Dispose();
            }
            
            foreach (var item in _journalValuesDatabases)
            {
                item?.Dispose();
            }
            
            _logger.Info($"{nameof(DatabaseCore)} dispposed");
        }
    }
}