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

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; init; }

        public KeyState KeyState { get; private set; }

        public KeyPermissions KeyPermissions { get; private set; }

        public string DisplayName { get; private set; }


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = entity.AuthorId;
            ProductId = entity.ProductId;
            KeyState = (KeyState)entity.KeyState;
            KeyPermissions = (KeyPermissions)entity.KeyPermissions;
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
            KeyState = KeyState.Active;
            KeyPermissions = KeyPermissions.CanAddProducts | KeyPermissions.CanAddSensors | KeyPermissions.CanSendSensorData;
            DisplayName = CommonConstants.DefaultAccessKey;
            ExpirationTime = DateTime.MaxValue;
        }


        public AccessKeyModel Update(AccessKeyUpdate model)
        {
            if (model.DisplayName != null)
                DisplayName = model.DisplayName;

            if (model.Permissions.HasValue)
                KeyPermissions = model.Permissions.Value;

            if (model.State.HasValue)
                KeyState = model.State.Value;

            return this;
        }

        internal AccessKeyEntity ToAccessKeyEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId,
                ProductId = ProductId,
                KeyState = (byte)KeyState,
                KeyPermissions = (long)KeyPermissions,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks
            };

        internal static AccessKeyModel BuildDefault(ProductModel product) => new AccessKeyModel(product);

        internal bool IsHasPermission(KeyPermissions permisssion, out string message)
        {
            message = string.Empty;
            if (!KeyPermissions.HasFlag(permisssion))
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
                KeyState = KeyState.Expired;
                return true;
            }

            return false;
        }

        internal bool HasPermissionForSendData(out string message)
            => !IsExpired(out message) && IsHasPermission(KeyPermissions.CanSendSensorData, out message);

        internal bool HasPermissionCreateProductBranch(out string message)
            => IsHasPermission(KeyPermissions.CanAddProducts, out message)
            && IsHasPermission(KeyPermissions.CanAddSensors, out message);
    }
}
