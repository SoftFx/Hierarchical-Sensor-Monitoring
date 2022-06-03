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

        public string Comment { get; }

        public KeyState State { get; }

        public KeyPermissions Permissions { get; }

        public string DisplayName { get; init; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; }


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = entity.AuthorId;
            ProductId = entity.ProductId;
            Comment = entity.Comment;
            State = (KeyState)entity.KeyState;
            Permissions = (KeyPermissions)entity.KeyPermissions;
            DisplayName = entity.DisplayName;
            CreationTime = new DateTime(entity.CreationTime);
            ExpirationTime = new DateTime(entity.ExpirationTime);
        }

        public AccessKeyModel(string authorId, string productId) : this()
        {
            AuthorId = authorId;
            ProductId = productId;
        }

        private AccessKeyModel()
        {
            Id = Guid.NewGuid();
            CreationTime = DateTime.UtcNow;
        }

        private AccessKeyModel(ProductModel product) : this()
        {
            AuthorId = product.AuthorId;
            ProductId = product.Id;
            Comment = CommonConstants.DefaultAccessKey;
            State = KeyState.Active;
            Permissions = KeyPermissions.CanAddProducts | KeyPermissions.CanSendSensorData;
            DisplayName = CommonConstants.DefaultAccessKey;
            ExpirationTime = DateTime.MaxValue;
        }


        internal AccessKeyEntity ToAccessKeyEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId,
                ProductId = ProductId,
                Comment = Comment,
                KeyState = (byte)State,
                KeyPermissions = (long)Permissions,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks
            };

        internal static AccessKeyModel BuildDefault(ProductModel product) => new AccessKeyModel(product);

        //ToDo
        internal bool IsHasPermission() => true;
    }
}
