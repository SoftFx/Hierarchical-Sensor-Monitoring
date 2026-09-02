using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class FailingDatabaseCore : IDatabaseCore
    {
        private readonly IDatabaseCore _inner;
        private readonly Func<SensorEntity, bool> _shouldFail;

        internal FailingDatabaseCore(IDatabaseCore inner, Func<SensorEntity, bool> shouldFail)
        {
            _inner = inner;
            _shouldFail = shouldFail;
        }

        public IDashboardCollection Dashboards => _inner.Dashboards;

        public ISnapshotDatabase Snapshots => _inner.Snapshots;

        public bool IsCompactRunning => _inner.IsCompactRunning;

        public bool IsExportRunning => _inner.IsExportRunning;

        public long SensorHistoryDbSize => _inner.SensorHistoryDbSize;

        public long JournalDbSize => _inner.JournalDbSize;

        public long ConfigDbSize => _inner.ConfigDbSize;

        public long BackupsSize => _inner.BackupsSize;

        public long TotalDbSize => _inner.TotalDbSize;

        public int SensorValuesPageCount => _inner.SensorValuesPageCount;

        public List<ISensorValuesDatabase> SensorValuesDatabases => _inner.SensorValuesDatabases;

        public IDatabaseSettings DatabaseSettings => _inner.DatabaseSettings;

        public TaskResult<string> BackupEnvironment(string backupPath) => _inner.BackupEnvironment(backupPath);

        public void AddFolder(FolderEntity entity) => _inner.AddFolder(entity);
        public void UpdateFolder(FolderEntity entity) => _inner.UpdateFolder(entity);
        public void RemoveFolder(string id) => _inner.RemoveFolder(id);
        public FolderEntity GetFolder(string id) => _inner.GetFolder(id);
        public List<FolderEntity> GetAllFolders() => _inner.GetAllFolders();

        public void AddProduct(ProductEntity entity) => _inner.AddProduct(entity);
        public void UpdateProduct(ProductEntity entity) => _inner.UpdateProduct(entity);
        public void RemoveProduct(string id) => _inner.RemoveProduct(id);
        public ProductEntity GetProduct(string id) => _inner.GetProduct(id);
        public List<ProductEntity> GetAllProducts() => _inner.GetAllProducts();

        public void RemoveAccessKey(Guid id) => _inner.RemoveAccessKey(id);
        public void AddAccessKey(AccessKeyEntity entity) => _inner.AddAccessKey(entity);
        public void UpdateAccessKey(AccessKeyEntity entity) => _inner.UpdateAccessKey(entity);
        public AccessKeyEntity GetAccessKey(Guid id) => _inner.GetAccessKey(id);
        public List<AccessKeyEntity> GetAccessKeys() => _inner.GetAccessKeys();

        // Optional injection point for API token write failures (operation name keyed),
        // used to verify persist-first publication: a failed write must leave neither
        // durable nor live state.
        internal Func<string, bool> ShouldFailApiTokenOp { get; set; }

        public bool TryInsertApiToken(ApiTokenEntity entity)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(TryInsertApiToken)) == true)
                throw new InvalidOperationException("Simulated DB failure for API token insert");

            return _inner.TryInsertApiToken(entity);
        }

        public bool TryRotateApiToken(ApiTokenEntity revokedOld, ApiTokenEntity replacement)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(TryRotateApiToken)) == true)
                throw new InvalidOperationException("Simulated DB failure for API token rotation");

            return _inner.TryRotateApiToken(revokedOld, replacement);
        }

        public void PutApiToken(ApiTokenEntity entity)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(PutApiToken)) == true)
                throw new InvalidOperationException("Simulated DB failure for API token update");

            _inner.PutApiToken(entity);
        }

        public ApiTokenEntity GetApiToken(string tokenId) => _inner.GetApiToken(tokenId);

        // Mirrors the real contract: a failed removal reports false instead of throwing.
        public bool RemoveApiToken(string tokenId)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(RemoveApiToken)) == true)
                return false;

            return _inner.RemoveApiToken(tokenId);
        }

        // Optional full override of the token scan result, for tests that need damaged
        // storage shapes (e.g. a row whose key disagrees with its payload TokenId).
        internal Func<List<(string KeyTokenId, ApiTokenEntity Entity)>> OverrideApiTokenScan { get; set; }

        public List<(string KeyTokenId, ApiTokenEntity Entity)> GetAllApiTokens()
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(GetAllApiTokens)) == true)
                throw new InvalidOperationException("Simulated DB failure for the API token scan");

            return OverrideApiTokenScan?.Invoke() ?? _inner.GetAllApiTokens();
        }

        public long GetGlobalRevocationGeneration()
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(GetGlobalRevocationGeneration)) == true)
                throw new InvalidOperationException("Simulated DB failure for generation read");

            return _inner.GetGlobalRevocationGeneration();
        }

        public long AdvanceGlobalRevocationGeneration()
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(AdvanceGlobalRevocationGeneration)) == true)
                throw new InvalidOperationException("Simulated DB failure for global generation advance");

            return _inner.AdvanceGlobalRevocationGeneration();
        }

        public long GetOwnerRevocationGeneration(Guid ownerUserId)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(GetOwnerRevocationGeneration)) == true)
                throw new InvalidOperationException("Simulated DB failure for owner generation read");

            return _inner.GetOwnerRevocationGeneration(ownerUserId);
        }

        public long AdvanceOwnerRevocationGeneration(Guid ownerUserId)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(AdvanceOwnerRevocationGeneration)) == true)
                throw new InvalidOperationException("Simulated DB failure for owner generation advance");

            return _inner.AdvanceOwnerRevocationGeneration(ownerUserId);
        }

        public void PutApiTokenSecurityEvent(ApiTokenSecurityEventEntity entity)
        {
            if (ShouldFailApiTokenOp?.Invoke(nameof(PutApiTokenSecurityEvent)) == true)
                throw new InvalidOperationException("Simulated DB failure for a security event write");

            _inner.PutApiTokenSecurityEvent(entity);
        }

        public List<ApiTokenSecurityEventEntity> ReadApiTokenSecurityEvents() =>
            _inner.ReadApiTokenSecurityEvents();

        public void AddSensor(SensorEntity entity)
        {
            if (_shouldFail(entity))
                throw new InvalidOperationException($"Simulated DB failure for sensor {entity.Id}");

            _inner.AddSensor(entity);
        }

        public void UpdateSensor(SensorEntity entity)
        {
            if (_shouldFail(entity))
                throw new InvalidOperationException($"Simulated DB failure for sensor {entity.Id}");

            _inner.UpdateSensor(entity);
        }

        public void RemoveSensorWithMetadata(Guid sensorId) => _inner.RemoveSensorWithMetadata(sensorId);
        public void AddSensorValue(Guid sensorId, BaseValue value) => _inner.AddSensorValue(sensorId, value);
        public void ClearSensorValues(Guid sensorId, DateTime from, DateTime to) => _inner.ClearSensorValues(sensorId, from, to);
        public byte[] GetLatestValue(Guid sensorId, long to) => _inner.GetLatestValue(sensorId, to);
        public byte[] GetFirstValue(Guid sensorId) => _inner.GetFirstValue(sensorId);
        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds) => _inner.GetLastAndFirstValues(sensorIds);
        public Dictionary<Guid, byte[]> GetLatestValuesFromTo(Dictionary<Guid, (long, long)> sensors) => _inner.GetLatestValuesFromTo(sensors);
        public IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count) => _inner.GetSensorValuesPage(sensorId, from, to, count);
        public IAsyncEnumerable<byte[]> GetSensorValues(Guid sensorId, DateTime from, DateTime to) => _inner.GetSensorValues(sensorId, from, to);
        public List<SensorEntity> GetAllSensors() => _inner.GetAllSensors();
        public void ExportValuesDatabase(string databaseName, Dictionary<Guid, string> sensors) => _inner.ExportValuesDatabase(databaseName, sensors);
        public (long dateCnt, long keySize, long valueSize) CalculateSensorHistorySize(Guid sensorId) => _inner.CalculateSensorHistorySize(sensorId);
        public IEnumerable<(byte[], byte[])> MigrateDatabaseV2() => _inner.MigrateDatabaseV2();

        public List<PolicyEntity> GetAllPolicies() => _inner.GetAllPolicies();
        public void AddPolicy(PolicyEntity policy) => _inner.AddPolicy(policy);
        public void UpdatePolicy(PolicyEntity policy) => _inner.UpdatePolicy(policy);
        public void RemovePolicy(Guid id) => _inner.RemovePolicy(id);

        public void AddUser(UserEntity user) => _inner.AddUser(user);
        public void UpdateUser(UserEntity user) => _inner.UpdateUser(user);
        public void RemoveUser(UserEntity user) => _inner.RemoveUser(user);
        public List<UserEntity> GetUsers() => _inner.GetUsers();
        public List<UserEntity> GetUsersPage(int page, int pageSize) => _inner.GetUsersPage(page, pageSize);

        public TelegramChatEntity GetTelegramChat(byte[] chatId) => _inner.GetTelegramChat(chatId);
        public List<TelegramChatEntity> GetTelegramChats() => _inner.GetTelegramChats();

        public SlackDestinationEntity GetSlackDestination(byte[] id) => _inner.GetSlackDestination(id);
        public List<SlackDestinationEntity> GetSlackDestinations() => _inner.GetSlackDestinations();

        public void AddChat(ChatEntity chat) => _inner.AddChat(chat);
        public void UpdateChat(ChatEntity chat) => _inner.UpdateChat(chat);
        public void RemoveChat(byte[] chatId) => _inner.RemoveChat(chatId);
        public ChatEntity GetChat(byte[] chatId) => _inner.GetChat(chatId);
        public List<ChatEntity> GetChats() => _inner.GetChats();

        public List<AlertTemplateEntity> GetAllAlertTemplates() => _inner.GetAllAlertTemplates();
        public void AddAlertTemplate(AlertTemplateEntity policy) => _inner.AddAlertTemplate(policy);
        public void RemoveAlertTemplate(Guid id) => _inner.RemoveAlertTemplate(id);

        public List<AlertScheduleEntity> GetAllAlertSchedules() => _inner.GetAllAlertSchedules();
        public AlertScheduleEntity GetAlertSchedule(Guid id) => _inner.GetAlertSchedule(id);
        public void AddAlertSchedule(AlertScheduleEntity schedule) => _inner.AddAlertSchedule(schedule);
        public void RemoveAlertSchedule(Guid id) => _inner.RemoveAlertSchedule(id);

        public void AddJournalValue(JournalKey journalKey, JournalRecordEntity value) => _inner.AddJournalValue(journalKey, value);
        public void RemoveJournalValues(Guid id, Guid parentId) => _inner.RemoveJournalValues(id, parentId);
        public IAsyncEnumerable<List<(byte[] Key, JournalRecordEntity Entity)>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType types, int count) => _inner.GetJournalValuesPage(sensorId, from, to, types, count);

        public void Compact() => _inner.Compact();

        public IEnumerable<(byte[], byte[])> GetAll() => _inner.GetAll();

        public void Dispose() => _inner.Dispose();
    }
}
