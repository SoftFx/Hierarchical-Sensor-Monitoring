using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;

namespace HSMServer.Dashboards.Panels.Modules
{
    public interface IPanelModule : IDisposable
    {
        Guid Id { get; }


        event Action UpdateEvent;
    }


    public abstract class BasePanelModule<TUpdate, TEntity> : IPanelModule
        where TUpdate : PanelSourceUpdate
        where TEntity : PanelBaseModuleEntity
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


        protected abstract void ApplyUpdate(TUpdate update);

        public abstract TEntity ToEntity();


        public void Update(TUpdate update)
        {
            ApplyUpdate(update);

            UpdateEvent?.Invoke();
        }


        public virtual void Dispose()
        {
            UpdateEvent = null;
        }
    }
}