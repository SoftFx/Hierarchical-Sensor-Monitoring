﻿using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IServerModel<EntityType, UpdateType>
    {
        Guid Id { get; }

        string Name { get; }


        EntityType ToEntity();

        void Update(UpdateType update);
    }


    public abstract class BaseServerModel<EntityType, UpdateType> : IServerModel<EntityType, UpdateType>
        where EntityType : BaseServerEntity, new()
        where UpdateType : BaseUpdateRequest
    {
        public Guid Id { get; }


        public Guid? AuthorId { get; init; }

        public DateTime CreationDate { get; init; }


        public string Name { get; private set; }

        public string Description { get; private set; }


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


        public virtual void Update(UpdateType update)
        {
            Name = update.Name ?? Name;
            Description = update.Description ?? Description;
        }


        public virtual EntityType ToEntity() => new()
        {
            Id = Id.ToByteArray(),
            Author = AuthorId?.ToByteArray(),
            CreationDate = CreationDate.Ticks,

            Name = Name,
            Description = Description,
        };
    }
}