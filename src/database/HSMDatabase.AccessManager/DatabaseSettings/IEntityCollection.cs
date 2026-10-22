using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseSettings
{
    public interface IDashboardCollection : IEntityCollection<DashboardEntity> { }

    // The undo-journal table (#1570). Unlike the dashboard collection, write
    // failures PROPAGATE (the journal's whole point is durability of undo
    // points) — see ChangeSetCollection in HSMDatabase.DatabaseWorkCore.
    public interface IChangeSetCollection : IEntityCollection<ChangeSetEntity> { }


    public interface IEntityCollection<T> where T : class, IBaseEntity
    {
        public void AddEntity(T entity);

        public void UpdateEntity(T entity);

        public void RemoveEntity(Guid id);


        public bool TryReadEntity(Guid id, out T entity);

        public List<T> ReadCollection();

        public TaskResult<string> Backup(string backupPath);
    }
}