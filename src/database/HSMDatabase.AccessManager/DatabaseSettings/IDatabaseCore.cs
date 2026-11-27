using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseCore : IDisposable
    {
        IDashboardCollection Dashboards { get; }

        // The undo journal of change sets (#1570). Write failures propagate
        // (unlike the dashboard collection) — see ChangeSetCollection.
        IChangeSetCollection ChangeSets { get; }

        // The download log (ADR-0022 point 15), a database of its own. Write failures propagate, as the change sets' do.
        IDownloadLogStore DownloadLog { get; }

        ISnapshotDatabase Snapshots { get; }

        public bool IsCompactRunning { get; }

        public bool IsExportRunning { get; }

        long SensorHistoryDbSize { get; }

        long JournalDbSize { get; }

        long ConfigDbSize { get; }

        long EnvironmentDbSize { get; }

        long ServerLayoutDbSize { get; }

        long BackupsSize { get; }

        long TotalDbSize { get; }

        // The block store of table sensors (#1844). Nothing is opened before its first use.
        HSMDatabase.AccessManager.TableBlocks.ITableBlockStore TableBlocks { get; }

        // The batch store of table sensors (#1850): the merged hours, under the same folder. Nothing is opened before its first use.
        HSMDatabase.AccessManager.TableBatches.ITableBatchStore TableBatches { get; }

        // The size of its folder on disk; 0 when it does not exist (nothing is opened).
        long TableBlocksDbSize { get; }

        // The large file store (#1883): files in a folder of their own, outside the databases. Nothing is created before
        // the first upload.
        HSMDatabase.AccessManager.LargeFiles.ILargeFileStore LargeFiles { get; }

        int SensorValuesPageCount { get; }

        List<ISensorValuesDatabase> SensorValuesDatabases { get; }

        TaskResult<string> BackupEnvironment(string backupPath);

        IDatabaseSettings DatabaseSettings { get; }

        #region Folders

        public void AddFolder(FolderEntity entity);
        public void UpdateFolder(FolderEntity entity);
        public void RemoveFolder(string id);
        public FolderEntity GetFolder(string id);
        public List<FolderEntity> GetAllFolders();

        #endregion

        #region Product

        void AddProduct(ProductEntity entity);
        void UpdateProduct(ProductEntity entity);
        void RemoveProduct(string id);
        ProductEntity GetProduct(string id);
        List<ProductEntity> GetAllProducts();

        #endregion

        #region AccessKey

        void RemoveAccessKey(Guid id);
        void AddAccessKey(AccessKeyEntity entity);
        void UpdateAccessKey(AccessKeyEntity entity);
        AccessKeyEntity GetAccessKey(Guid id);
        List<AccessKeyEntity> GetAccessKeys();

        #endregion

        #region Api tokens

        // Persist-first token creation: false on TokenId collision, throws on write failure.
        bool TryInsertApiToken(ApiTokenEntity entity);

        // Atomic old-revoke + replacement-insert in one write batch.
        bool TryRotateApiToken(ApiTokenEntity revokedOld, ApiTokenEntity replacement);

        // Full-row lifecycle update (revoke, restrict); propagates storage failures.
        void PutApiToken(ApiTokenEntity entity);

        // Authentication-path read; null means missing or unreadable (fail closed).
        ApiTokenEntity GetApiToken(string tokenId);

        // Retention removal. True = the durable row is gone (deleted or already absent);
        // false = the removal failed and the row may still exist — the caller must NOT
        // unpublish the live record in that case, or the row rejoins the index after restart.
        bool RemoveApiToken(string tokenId);

        // Full scan to rebuild the authentication index at startup. Corrupt records are
        // skipped; a scan failure throws so the caller can fail the index closed. Each
        // record comes with the token id of the key it was stored under, so the loader
        // can reject a row whose key disagrees with its payload TokenId.
        List<(string KeyTokenId, ApiTokenEntity Entity)> GetAllApiTokens();

        long GetGlobalRevocationGeneration();
        long AdvanceGlobalRevocationGeneration();
        long GetOwnerRevocationGeneration(Guid ownerUserId);
        long AdvanceOwnerRevocationGeneration(Guid ownerUserId);

        // Append-only per-request security events of the API-token channel.
        void PutApiTokenSecurityEvent(ApiTokenSecurityEventEntity entity);

        // Chronological scan of all security events (retention/query surface).
        List<ApiTokenSecurityEventEntity> ReadApiTokenSecurityEvents();

        // Retention primitive: removes up to `limit` events strictly older than the
        // cutoff ticks (chronological key order makes this a bounded prefix-range
        // delete) and returns the number removed. Bounded so one sweep stays cheap on a
        // large table; the caller repeats until it returns less than the limit.
        int RemoveApiTokenSecurityEventsBefore(long ticksCutoffUtc, int limit);

        #endregion

        #region Dump passwords

        // The dump passwords of products (ADR-0022 point 13), apart from the product entity. A scan failure throws; a row
        // that cannot be parsed is reported by its product; the put and the remove THROW on failure (unlike
        // UpdateProduct), so a change of a password fails visibly.
        DumpPasswordRows GetAllDumpPasswords();
        void PutDumpPasswords(ProductDumpPasswordsEntity entity);
        void RemoveDumpPasswords(Guid productId);

        #endregion

        #region Sensors

        void AddSensor(SensorEntity entity);
        void UpdateSensor(SensorEntity entity);

        /// <summary>
        /// <see cref="AddSensor"/> and <see cref="UpdateSensor"/> with the outcome of the write (#1756):
        /// null when the row was written, otherwise the failure. The void methods do not report a
        /// write the storage engine failed on (it is logged there and the caller is not told), and
        /// every caller but one relies on that. The one is the registration of a sensor by a
        /// collector, which must tell a row that was not written from one that was. A failed write is
        /// reported, not thrown; null is the answer of success, so a store that does not implement
        /// the report (a loose test double) reads as "written", as with the void methods.
        /// </summary>
        Exception AddSensorChecked(SensorEntity entity);
        Exception UpdateSensorChecked(SensorEntity entity);
        void RemoveSensorWithMetadata(Guid sensorId);

        /// <summary>
        /// Whether the sensor's entity is in the database (#1883): true when its key is there (also when the entity cannot be
        /// read back), false only when a read that succeeded did not find the key, null when the read failed (cannot tell).
        /// </summary>
        bool? SensorEntityExists(Guid sensorId);

        /// <summary>
        /// Every sensor entity the database stores, with its type, whether or not the list of sensors names it: null when a
        /// row stored under an id may be a sensor's but cannot be read (#1913: the check of large values at start cannot tell
        /// about such a sensor). Throws when the database cannot be walked.
        /// </summary>
        List<(Guid Id, byte? Type)> GetSensorTypes();

        /// <summary>
        /// Whether a values database (a history week) the server knows covers <paramref name="ticks"/> (#1913). When none
        /// does, <see cref="GetSensorValue"/> answers "no row" without having read anything: the week was never written, or
        /// its folder was lost. Opens or creates no week database.
        /// </summary>
        bool HasSensorValuesDatabaseAt(long ticks);

        /// <summary>
        /// Whether the entity of a sensor or of a product (node) with the id is in the environment database (#1850 part 4d:
        /// a table removal is acted on only once its entity is gone). True when the read fails.
        /// </summary>
        bool EntityExists(Guid id);

        void AddSensorValue(Guid sensorId, BaseValue value);
        void ClearSensorValues(Guid sensorId, DateTime from, DateTime to);

        /// <summary>
        /// Copy every history row of <paramref name="fromSensorId"/> under
        /// <paramref name="intoSensorId"/>, same tick, same bytes (sensor-move merge,
        /// #1720). A tick <paramref name="intoSensorId"/> already has keeps its own row
        /// and counts as a collision. The source rows are left in place.
        /// </summary>
        (long Copied, long Collisions) MergeSensorValues(Guid fromSensorId, Guid intoSensorId);

        byte[] GetLatestValue(Guid sensorId, long to);

        /// <summary>
        /// The stored row of a sensor at exactly <paramref name="ticks"/> (#1883): null when there is none, an empty array
        /// when it could not be read (a read error is logged by the store). Opens or creates no week database.
        /// </summary>
        byte[] GetSensorValue(Guid sensorId, long ticks);

        byte[] GetFirstValue(Guid sensorId);

        Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds);

        Dictionary<Guid, byte[]> GetLatestValuesFromTo(Dictionary<Guid, (long, long)> sensors);

        IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count);

        IAsyncEnumerable<byte[]> GetSensorValues(Guid sensorId, DateTime from, DateTime to);

        /// <summary>
        /// The stored values of a sensor, its whole history week by week (newest week first, each week oldest first), whose bytes start with
        /// <paramref name="firstByte"/> (#1913: the large values of a File sensor, <c>LargeFileValue.UnionTag</c>). Every
        /// other value is passed by in the database without being copied or deserialized. A read that fails throws;
        /// <paramref name="token"/> is asked at every row, also at the rows passed by.
        /// </summary>
        IAsyncEnumerable<byte[]> GetSensorValuesWithFirstByte(Guid sensorId, byte firstByte, System.Threading.CancellationToken token = default);

        List<SensorEntity> GetAllSensors();

        void ExportValuesDatabase(string databaseName, Dictionary<Guid, string> sensors);

        (long dateCnt, long keySize, long valueSize) CalculateSensorHistorySize(Guid sensorId);


        IEnumerable<(byte[], byte[])> MigrateDatabaseV2();

        #endregion

        #region Policies
        List<PolicyEntity> GetAllPolicies();

        // Direct row fetch by id, bypassing the global policy-id index — the
        // boot path's self-heal arm (#1407): a row whose index entry was lost
        // to a concurrent-index race is invisible to GetAllPolicies but still
        // referenced by the sensor entity, and this reads it back.
        PolicyEntity GetPolicy(Guid id);

        // The same fetch with "absent" and "unreadable" told apart: false
        // means no row under the id, a throw means the row could not be
        // read. The self-heal must not mistake a read failure for a true
        // orphan (#1407).
        bool TryGetPolicy(Guid id, out PolicyEntity entity);

        void AddPolicy(PolicyEntity policy);
        void UpdatePolicy(PolicyEntity policy);
        void RemovePolicy(Guid id);

        #endregion

        #region User

        void AddUser(UserEntity user);
        void UpdateUser(UserEntity user);
        void RemoveUser(UserEntity user);
        List<UserEntity> GetUsers();
        List<UserEntity> GetUsersPage(int page, int pageSize);

        #endregion

        #region Telegram chat

        TelegramChatEntity GetTelegramChat(byte[] chatId);
        List<TelegramChatEntity> GetTelegramChats();

        #endregion

        #region Slack destination

        SlackDestinationEntity GetSlackDestination(byte[] id);
        List<SlackDestinationEntity> GetSlackDestinations();

        #endregion

        #region Chat

        void AddChat(ChatEntity chat);
        void UpdateChat(ChatEntity chat);
        void RemoveChat(byte[] chatId);
        ChatEntity GetChat(byte[] chatId);
        List<ChatEntity> GetChats();

        #endregion

        #region Alert Templates
        List<AlertTemplateEntity> GetAllAlertTemplates();

        // Create-path upsert: logs and swallows storage failures.
        void AddAlertTemplate(AlertTemplateEntity policy);

        // Overwrite of an EXISTING template row that PROPAGATES storage
        // failures instead of logging and swallowing them: callers that must
        // not silently diverge memory from storage (the #1409 schedule
        // detach) need the failure surfaced to keep their persist-first
        // ordering retryable. The template must already exist — the id-list
        // add of the AddAlertTemplate upsert is not repeated here.
        void UpdateAlertTemplate(AlertTemplateEntity entity);

        void RemoveAlertTemplate(Guid id);
        #endregion

        #region Schedules

        List<AlertScheduleEntity> GetAllAlertSchedules();
        AlertScheduleEntity GetAlertSchedule(Guid id);
        void AddAlertSchedule(AlertScheduleEntity schedule);
        void RemoveAlertSchedule(Guid id);

        #endregion Schedules

        #region Journal

        void AddJournalValue(JournalKey journalKey, JournalRecordEntity value);

        void RemoveJournalValues(Guid id, Guid parentId);

        IAsyncEnumerable<List<(byte[] Key, JournalRecordEntity Entity)>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType types, int count);

        /// <summary>
        /// Every CHANGES-type journal record of the time range across ALL
        /// nodes (the global Journal page's read, #1588). The read walks the
        /// overlapping shards in full and refines by record type and time in
        /// memory — the guid-major key layout leaves no narrower range.
        /// </summary>
        List<(byte[] Key, JournalRecordEntity Entity)> GetAllJournalValues(DateTime from, DateTime to);

        /// <summary>Point read of one journal record by its exact storage key (#1588); false when absent.</summary>
        bool TryGetJournalValue(JournalKey key, out JournalRecordEntity entity);

        #endregion

        void Compact();

        /// <summary>
        /// Compacts at most one closed weekly shard (sensor history or journal) that is due:
        /// never compacted, or changed since its last compaction, and quiet for
        /// <paramref name="quietPeriod"/> (#1801). Shares <see cref="IsCompactRunning"/> with
        /// <see cref="Compact"/>, so the two never overlap; answers Busy when the other runs and
        /// ShuttingDown once disposal has begun (disposal waits for a running compaction).
        /// </summary>
        HistoryShardCompactionOutcome CompactNextDueHistoryShard(DateTime utcNow, TimeSpan quietPeriod);

        /// <summary>
        /// One pass of the sealing job of sensor history (ADR-0020, #1804): at most one action on one week (finish an
        /// unseal, remove source rows after the keep period, seal a closed week, fold a sealed week's overlay). Shares
        /// the compaction state with <see cref="Compact"/> and <see cref="CompactNextDueHistoryShard"/>, so no two of
        /// them ever work at the same time; answers Busy when another one runs. Does nothing when
        /// <see cref="HistorySealingSettings.Enabled"/> is false.
        /// </summary>
        HistorySealingPassReport RunHistorySealingPass(DateTime utcNow, HistorySealingSettings settings);

        /// <summary>
        /// Turns a sealed week back into rows (the operator's unseal, #1804) and keeps the job from sealing it again.
        /// <paramref name="weekName"/> is the row database name, as <see cref="SensorValuesDatabases"/> lists it.
        /// </summary>
        HistorySealingPassReport UnsealHistoryWeek(string weekName, DateTime utcNow);

        /// <summary>The last pass of the sealing job or unseal in this process; null before the first.</summary>
        HistorySealingPassReport LastHistorySealingPass { get; }


        IEnumerable<(byte[], byte[])> GetAll();

    }
}