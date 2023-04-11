using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;

namespace HSMServer.Core.Model
{
    [Flags]
    public enum KeyPermissions : long
    {
        CanSendSensorData = 1,
        CanAddNodes = 2,
        CanAddSensors = 4,
        CanReadSensorData = 8,
        CanUseGrafana = 16
    }

    public enum KeyState : byte
    {
        Active = 0,
        Expired = 1,
        Blocked = 7
    }


    public class AccessKeyModel
    {
        internal static InvalidAccessKey InvalidKey { get; } = new();


        public static KeyPermissions FullPermissions { get; } =
            (KeyPermissions)(1 << Enum.GetValues<KeyPermissions>().Length) - 1;


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public Guid ProductId { get; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; init; }

        public KeyState State { get; private set; }

        public KeyPermissions Permissions { get; private set; }

        public string DisplayName { get; private set; }


        public bool IsExpired => DateTime.UtcNow >= ExpirationTime;


        public AccessKeyModel(AccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            ProductId = Guid.Parse(entity.ProductId);
            State = (KeyState)entity.State;
            Permissions = (KeyPermissions)entity.Permissions;
            DisplayName = entity.DisplayName;
            CreationTime = new DateTime(entity.CreationTime);
            ExpirationTime = new DateTime(entity.ExpirationTime);
        }

        public AccessKeyModel(Guid authorId, Guid productId) : this()
        {
            AuthorId = authorId;
            ProductId = productId;
        }

        protected AccessKeyModel()
        {
            Id = Guid.NewGuid();
            CreationTime = DateTime.UtcNow;
        }

        private AccessKeyModel(ProductModel product) : this()
        {
            AuthorId = product.AuthorId;
            ProductId = product.Id;
            State = KeyState.Active;
            Permissions = FullPermissions;
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
                AuthorId = AuthorId.ToString(),
                ProductId = ProductId.ToString(),
                State = (byte)State,
                Permissions = (long)Permissions,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks
            };

        internal static AccessKeyModel BuildDefault(ProductModel product) => new AccessKeyModel(product);

        internal bool IsHasPermissions(KeyPermissions expectedPermissions, out string message)
        {
            var common = expectedPermissions & Permissions;
            message = string.Empty;

            if (common != expectedPermissions)
                message = $"AccessKey doesn't have {expectedPermissions & ~common}.";


            return string.IsNullOrEmpty(message);
        }

        internal bool CheckExpired(out string message)
        {
            message = string.Empty;

            if (ExpirationTime < DateTime.UtcNow)
            {
                message = "AccessKey expired.";
                State = KeyState.Expired;
            }

            return !string.IsNullOrEmpty(message);
        }

        internal bool CheckBlocked(out string message)
        {
            message = string.Empty;

            if (State == KeyState.Blocked)
                message = "AccessKey is blocked.";

            return !string.IsNullOrEmpty(message);
        }

        internal virtual bool IsValid(KeyPermissions permissions, out string message) =>
            !CheckBlocked(out message) && !CheckExpired(out message) && IsHasPermissions(permissions, out message);
    }

    public class InvalidAccessKey : AccessKeyModel
    {
        internal override bool IsValid(KeyPermissions permissions, out string message)
        {
            message = "Key is invalid.";
            return false;
        }
    }
}