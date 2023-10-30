using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMDatabase.LevelDB;
using NLog;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMDatabase.DatabaseWorkCore
{
    internal sealed class DashboardCollection : EntityCollection<DashboardEntity>, IDashboardsCollection
    {
        protected override string TableName => "ServerDashboards";


        internal DashboardCollection(string dbName) : base(dbName) { }
    }



    internal abstract class EntityCollection<T> : IEntityCollection<T> where T : class, IBaseEntity
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            IgnoreReadOnlyProperties = true,
        };

        private readonly HashSet<Guid> _idsHash = new(1 << 4);
        private readonly byte[] _tableId;

        private readonly IEntityDatabase _database;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        protected abstract string TableName { get; }


        protected EntityCollection(string dbName)
        {
            _database = LevelDBManager.GetEntityDatabase(dbName);
            _tableId = Encoding.UTF8.GetBytes(TableName);

            _idsHash = LoadHash();
        }


        public void AddEntity(T entity)
        {
            try
            {
                RegisterId(new Guid(entity.Id));

                _database.Put(entity.Id, ToBytes(entity));
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        public void RemoveEntity(Guid id)
        {
            try
            {
                RemoveId(id);

                _database.Delete(id.ToByteArray());
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        public void UpdateEntity(T entity) => _database.Put(entity.Id, ToBytes(entity));


        public bool TryReadEntity(Guid id, out T entity)
        {
            entity = null;

            try
            {
                var canRead = _database.TryRead(id.ToByteArray(), out var bytes);

                if (canRead)
                    entity = FromBytes(bytes);

                return canRead;
            }
            catch (Exception ex)
            {
                LogError(ex);

                return false;
            }
        }

        public List<T> ReadCollection()
        {
            var list = new List<T>(1 << 2);

            foreach (var id in _idsHash)
                if (TryReadEntity(id, out var entity))
                    list.Add(entity);

            return list;
        }


        private void RegisterId(Guid id)
        {
            if (_idsHash.Add(id))
            {
                LogInfo($"{id} has been registred in list");
                ResaveIdSet();
            }
            else
                LogError($"{id} already exsists");
        }

        private void RemoveId(Guid id)
        {
            if (_idsHash.Remove(id))
            {
                LogInfo($"{id} has been removed from list");
                ResaveIdSet();
            }
            else
                LogError($"{id} not found");
        }

        private void ResaveIdSet()
        {
            _database.Put(_tableId, JsonSerializer.SerializeToUtf8Bytes(_idsHash));

            LogInfo($"Id list has been uploaded");
        }

        private HashSet<Guid> LoadHash() => _database.TryRead(_tableId, out var table) ?
            JsonSerializer.Deserialize<HashSet<Guid>>(table, _options) : new HashSet<Guid>();


        private static byte[] ToBytes(T entity) => JsonSerializer.SerializeToUtf8Bytes(entity, _options);

        private static T FromBytes(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, _options);


        private void LogInfo(string message, [CallerMemberName] string callerName = null) => _logger.Info($"{callerName}. {message}");

        private void LogError(string message, [CallerMemberName] string callerName = null) => _logger.Error($"{callerName}. {message}");

        private void LogError(Exception ex, [CallerMemberName] string callerName = null) => _logger.Error($"{callerName}. {ex}");
    }
}