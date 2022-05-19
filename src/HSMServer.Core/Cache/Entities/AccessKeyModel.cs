using HSMCommon.Constants;
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

        public bool IsLocked { get; }

        public KeyRolesEnum KeyRole { get; }

        public string DisplayName { get; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; }


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

        private AccessKeyModel(ProductModel product)
        {
            Id = Guid.NewGuid();
            AuthorId = product.AuthorId;
            ProductId = product.Id;
            IsLocked = false;
            KeyRole = KeyRolesEnum.Admin;
            DisplayName = CommonConstants.DefaultAccessKey;
            CreationTime = DateTime.UtcNow;
            ExpirationTime = DateTime.MaxValue;
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

        internal static AccessKeyModel BuildDefault(ProductModel product) => new AccessKeyModel(product);
    }
}
