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

        public void AddTelegramChat(TelegramChatEntity chat) => _inner.AddTelegramChat(chat);
        public void UpdateTelegramChat(TelegramChatEntity chat) => _inner.UpdateTelegramChat(chat);
        public void RemoveTelegramChat(byte[] chatId) => _inner.RemoveTelegramChat(chatId);
        public TelegramChatEntity GetTelegramChat(byte[] chatId) => _inner.GetTelegramChat(chatId);
        public List<TelegramChatEntity> GetTelegramChats() => _inner.GetTelegramChats();

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
