using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;

namespace HSMServer.Dashboards
{
    public interface IPanelModule : IDisposable
    {
        Guid Id { get; }


        event Action UpdateEvent;
    }


    public abstract class BasePanelModule<TUpdate, TEntity> : IPanelModule
        where TUpdate : PanelSourceUpdate
        where TEntity : PanelBaseModuleEntity, new()
    {
        public Guid Id { get; }


        public event Action UpdateEvent;


        protected BasePanelModule()
        {
            Id = Guid.NewGuid();
        }

        protected BasePanelModule(TEntity entity)
        {
            Id = new Guid(entity.Id);
        }


        public abstract void Update(TUpdate update);


        public void NotifyUpdate(TUpdate update)
        {
            Update(update);

            UpdateEvent?.Invoke();
        }

        public virtual void Dispose() => UpdateEvent = null;

        public virtual TEntity ToEntity() => new()
        {
            Id = Id.ToByteArray()
        };
    }
}