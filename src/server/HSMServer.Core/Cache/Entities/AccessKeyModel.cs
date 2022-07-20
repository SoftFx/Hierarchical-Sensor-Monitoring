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
        CanAddNodes = 2,
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

        public KeyState State { get; private set; }

        public KeyPermissions Permissions { get; private set; }

        public string DisplayName { get; private set; }


        public bool HasExpired => DateTime.UtcNow >= ExpirationTime && State < KeyState.Expired;


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = entity.AuthorId;
            ProductId = entity.ProductId;
            State = (KeyState)entity.State;
            Permissions = (KeyPermissions)entity.Permissions;
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
            State = KeyState.Active;
            Permissions = KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors | KeyPermissions.CanSendSensorData;
            DisplayName = CommonConstants.DefaultAccessKey;
            ExpirationTime = DateTime.MaxValue;
        }


        public AccessKeyModel Update(AccessKeyUpdate model)
        {
            if (model.DisplayName != null)
                DisplayName = model.DisplayName;

            if (model.Permissions.HasValue)
                Permissions = model.Permissions.Value;

            if (model.State.HasValue)
                State = model.State.Value;

            return this;
        }

        internal AccessKeyEntity ToAccessKeyEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId,
                ProductId = ProductId,
                State = (byte)State,
                Permissions = (long)Permissions,
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
                return true;
            }

            return false;
        }

        internal bool HasPermissionForSendData(out string message)
            => !IsExpired(out message) && IsHasPermission(KeyPermissions.CanSendSensorData, out message);

        internal bool HasPermissionCreateProductBranch(out string message)
            => IsHasPermission(KeyPermissions.CanAddNodes, out message)
            && IsHasPermission(KeyPermissions.CanAddSensors, out message);
    }
}
