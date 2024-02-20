using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IServerModel<EntityType, UpdateType> : IDisposable
    {
        Guid Id { get; }

        string Name { get; }


        EntityType ToEntity();

        void Update(UpdateType update);
    }


    public interface INotifyServerModel<EntityType, UpdateType> : IServerModel<EntityType, UpdateType>
    {
        event Action UpdatedEvent;


        void NotifyUpdate(UpdateType update);
    }


    public abstract class BaseServerModel<EntityType, UpdateType> : INotifyServerModel<EntityType, UpdateType>
        where EntityType : BaseServerEntity, new()
        where UpdateType : BaseUpdateRequest
    {
        public Guid Id { get; }


        public Guid? AuthorId { get; init; }

        public DateTime CreationDate { get; init; }


        public string Name { get; private set; }

        public string Description { get; private set; }


        public event Action UpdatedEvent;


        protected BaseServerModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        protected BaseServerModel(EntityType entity)
        {
            Id = new Guid(entity.Id);
            AuthorId = entity.Author is not null ? new Guid(entity.Author) : null;
            CreationDate = new DateTime(entity.CreationDate);

            Name = entity.Name;
            Description = entity.Description;
        }

        protected BaseServerModel(BaseAddRequest add) : this()
        {
            Name = add.Name;
            AuthorId = add.AuthorId;
            Description = add.Description;
        }


        public void Update(UpdateType update)
        {
            Name = update.Name ?? Name;
            Description = update.Description ?? Description;

            ApplyUpdate(update);
        }

        public void NotifyUpdate(UpdateType update)
        {
            Update(update);
            ThrowUpdateEvent();
        }

        protected virtual void ApplyUpdate(UpdateType update) { }


        public virtual EntityType ToEntity() => new()
        {
            Id = Id.ToByteArray(),
            Author = AuthorId?.ToByteArray(),
            CreationDate = CreationDate.Ticks,

            Name = Name,
            Description = Description,
        };

        public virtual void Dispose() => UpdatedEvent = null;


        protected void ThrowUpdateEvent() => UpdatedEvent?.Invoke();
    }
}