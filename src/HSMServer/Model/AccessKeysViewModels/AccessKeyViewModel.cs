using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.AccessKeysViewModels
{
    public class AccessKeyViewModel
    {
        private readonly DateTime _expirationTime;


        public Guid Id { get; }

        public string ProductId { get; }

        public string ProductName { get; }

        public string AuthorName { get; }

        public string ExpirationDate { get; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public string Permissions { get; private set; }

        public KeyState State { get; private set; }

        public bool IsChangeAvailable { get; internal set; }


        internal AccessKeyViewModel(AccessKeyModel accessKey, string productName, string authorName)
        {
            _expirationTime = accessKey.ExpirationTime;

            Id = accessKey.Id;
            ProductId = accessKey.ProductId;
            ProductName = productName;
            AuthorName = authorName;
            ExpirationDate = BuildExpiration(accessKey.ExpirationTime);

            Update(accessKey);
        }


        internal void Update(AccessKeyModel accessKey)
        {
            DisplayName = accessKey.DisplayName;
            Description = accessKey.Comment;
            Permissions = BuildPermissions(accessKey.KeyPermissions);
            State = accessKey.KeyState;
        }

        internal bool HasExpired() => DateTime.UtcNow >= _expirationTime && State < KeyState.Expired;

        internal static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimited)
                : expirationTime.ToString();

        private static string BuildPermissions(KeyPermissions permissions)
        {
            var result = new List<string>(2);

            if (permissions.HasFlag(KeyPermissions.CanSendSensorData))
                result.Add(nameof(KeyPermissions.CanSendSensorData));
            if (permissions.HasFlag(KeyPermissions.CanAddProducts))
                result.Add(nameof(KeyPermissions.CanAddProducts));
            if (permissions.HasFlag(KeyPermissions.CanAddSensors))
                result.Add(nameof(KeyPermissions.CanAddSensors));

            return string.Join(", ", result);
        }
    }
}
