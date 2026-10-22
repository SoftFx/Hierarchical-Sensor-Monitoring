using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMDatabase.LevelDB;
using HSMCommon.Collections;
using HSMCommon.TaskResult;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMDatabase.DatabaseWorkCore
{
    // The undo-journal table (#1570): one row per change set, keyed by the
    // change-set id, plus the id set under the table-name key (the
    // EntityCollection layout). DELIBERATELY not derived from EntityCollection:
    // that base swallows storage exceptions after logging, which would turn a
    // disk failure into a silent loss of undo points — the one thing this
    // table exists to guarantee. WRITE failures propagate to the caller (the
    // ChangeSetJournal decides how to surface them); a READ of one corrupt row
    // is skipped so one damaged entry cannot take the whole list surface down.
    internal sealed class ChangeSetCollection : IChangeSetCollection
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            IgnoreReadOnlyProperties = true,
        };

        private readonly object _sync = new();
        private readonly CHash<Guid> _ids;
        private readonly byte[] _tableId;

        private readonly IEntityDatabase _database;

        private const string TableName = "ChangeSets";


        internal ChangeSetCollection(string dbName)
        {
            _database = LevelDBManager.GetEntityDatabase(dbName);
            _tableId = Encoding.UTF8.GetBytes(TableName);

            _ids = _database.TryRead(_tableId, out var table)
                ? JsonSerializer.Deserialize<CHash<Guid>>(table, _options) ?? new()
                : new CHash<Guid>();
        }


        public void AddEntity(ChangeSetEntity entity)
        {
            lock (_sync)
            {
                var id = new Guid(entity.Id);

                if (!_ids.Add(id))
                    throw new InvalidOperationException($"Change set {id} already exists in the journal.");

                // Roll the in-memory id back out when the row never landed:
                // a stuck id would make every later Append to it miss on
                // TryReadEntity and die on "already exists" — the group's
                // undo points lost until a restart.
                try
                {
                    _database.Put(entity.Id, ToBytes(entity));
                }
                catch
                {
                    _ids.Remove(id);
                    throw;
                }

                ResaveIdSet();
            }
        }

        public void UpdateEntity(ChangeSetEntity entity)
        {
            lock (_sync)
            {
                _database.Put(entity.Id, ToBytes(entity));
            }
        }

        public void RemoveEntity(Guid id)
        {
            lock (_sync)
            {
                if (!_ids.Remove(id))
                    return;

                _database.Delete(id.ToByteArray());
                ResaveIdSet();
            }
        }

        public bool TryReadEntity(Guid id, out ChangeSetEntity entity)
        {
            entity = null;

            try
            {
                var canRead = _database.TryRead(id.ToByteArray(), out var bytes);

                if (canRead)
                    entity = FromBytes(bytes);

                return canRead && entity is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public List<ChangeSetEntity> ReadCollection()
        {
            var list = new List<ChangeSetEntity>();

            // Snapshot the ids under the lock: a concurrent Add/Remove mutates
            // the set, and enumerating it directly would throw
            // InvalidOperationException mid-iteration (the read itself is
            // per-id and unlocked on purpose — the parse of one row must not
            // hold up writers).
            Guid[] ids;

            lock (_sync)
            {
                ids = [.. _ids];
            }

            foreach (var id in ids)
                if (TryReadEntity(id, out var entity))
                    list.Add(entity);

            return list;
        }

        public TaskResult<string> Backup(string backupPath) => _database.Backup(backupPath);


        private void ResaveIdSet() => _database.Put(_tableId, JsonSerializer.SerializeToUtf8Bytes(_ids, _options));

        private static byte[] ToBytes(ChangeSetEntity entity) => JsonSerializer.SerializeToUtf8Bytes(entity, _options);

        private static ChangeSetEntity FromBytes(byte[] bytes) => JsonSerializer.Deserialize<ChangeSetEntity>(bytes, _options);
    }
}
