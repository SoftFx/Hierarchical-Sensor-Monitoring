using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Core.Cache.Entities
{
    public sealed class AccessKeyModel
    {
        public Guid Id { get; init; }

        public string AuthorId { get; }

        public string ProductId { get; set; }

        public bool IsLocked { get; }

        public KeyRolesEnum KeyRole { get; }

        public string DisplayName { get; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; }


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = entity.AuthorId;
            ProductId = entity.ProductId ?? string.Empty;
            IsLocked = entity.IsLocked;
            KeyRole = (KeyRolesEnum)entity.KeyRole;
            DisplayName = entity.DisplayName;
            CreationTime = new DateTime(entity.CreationTime);
            ExpirationTime = new DateTime(entity.ExpirationTime);
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
