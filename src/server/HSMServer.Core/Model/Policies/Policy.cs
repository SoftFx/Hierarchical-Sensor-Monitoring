using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model
{
    public abstract class Policy
    {
        public Guid Id { get; init; }

        public string Type { get; init; }


        internal Policy()
        {
            Id = Guid.NewGuid();
            Type = GetType().Name;
        }


        internal PolicyEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                Policy = this,
            };
    }


    public abstract class Policy<T> : Policy where T : BaseValue
    {
        internal Policy() : base() { }
    }
}
