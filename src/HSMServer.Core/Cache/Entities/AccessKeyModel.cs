using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public sealed class AccessKeyModel
    {
        public Guid Id { get; }

        public string AuthorId { get; }

        public string ProductId { get; }

        public bool IsLocked { get; private set; }

        public KeyRolesEnum KeyRole { get; private set; }

        public string DisplayName { get; private set; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; private set; }


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = entity.AuthorId;
            ProductId = entity.ProductId;
            IsLocked = entity.IsLocked;
            KeyRole = (KeyRolesEnum)entity.KeyRole;
            DisplayName = entity.DisplayName;
            CreationTime = new DateTime(entity.CreationTime);
            ExpirationTime = new DateTime(entity.ExpirationTime);
        }

        internal void Update(AccessKeyModel key)
        {
            IsLocked = key.IsLocked;
            KeyRole = key.KeyRole;
            DisplayName = key.DisplayName;
            ExpirationTime = key.ExpirationTime;
        }

        internal AccessKeyEntity ToAccessKeyEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId,
                ProductId = ProductId,
                IsLocked = IsLocked,
                KeyRole = (byte)KeyRole,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks
            };
    }
}
