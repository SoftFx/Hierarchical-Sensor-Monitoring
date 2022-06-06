using HSMCommon.Attributes;
using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Cache.Entities
{
    [SwaggerIgnore]
    [Flags]
    public enum KeyPermissions : long
    {
        CanSendSensorData = 1,
        CanAddProducts = 2,
        CanAddSensors = 4
    }

    [SwaggerIgnore]
    public enum KeyState : byte
    {
        Active = 0,
        Expired = 1,
        Blocked = 7
    }


    public sealed class AccessKeyModel
    {
        public Guid Id { get; }

        public string AuthorId { get; }

        public string ProductId { get; }

        public string Comment { get; }

        public KeyState State { get; private set; }

        public KeyPermissions Permissions { get; }

        public string DisplayName { get; }

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

        private AccessKeyModel(ProductModel product)
        {
            Id = Guid.NewGuid();
            AuthorId = product.AuthorId;
            ProductId = product.Id;
            Comment = CommonConstants.DefaultAccessKey;
            State = KeyState.Active;
            Permissions = KeyPermissions.CanAddProducts | KeyPermissions.CanSendSensorData;
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
                Comment = Comment,
                KeyState = (byte)State,
                KeyPermissions = (long)Permissions,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks
            };

        internal static AccessKeyModel BuildDefault(ProductModel product) => new AccessKeyModel(product);

        internal bool IsHasPermission(KeyPermissions permisssion, out string message)
        {
            message = string.Empty;

            if (!Permissions.HasFlag(permisssion))
            {
                message = $"AccessKey doesn't have {permisssion}.";
                return false;
            }

            return true;
        }

        internal bool IsExpired(out string message)
        {
            message = string.Empty;

            if (ExpirationTime < DateTime.UtcNow)
            {
                message = "AccessKey expired.";
                State = KeyState.Expired;
                return false;
            }

            return true;
        }
    }
}
