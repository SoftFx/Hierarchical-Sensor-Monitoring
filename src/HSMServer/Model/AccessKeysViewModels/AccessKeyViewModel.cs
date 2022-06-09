using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.AccessKeysViewModels
{
    public class AccessKeyViewModel
    {
        public Guid Id { get; }

        public string ProductId { get; }

        public string ProductName { get; }

        public string AuthorName { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public string ExpirationDate { get; }

        public string Permissions { get; }

        public KeyState State { get; }

        public bool IsRemovingAccessKeyAvailable { get; internal set; }


        internal AccessKeyViewModel(AccessKeyModel accessKey, string productName, string authorName)
        {
            Id = accessKey.Id;
            ProductId = accessKey.ProductId;
            ProductName = productName;
            AuthorName = authorName;
            DisplayName = accessKey.DisplayName;
            Description = accessKey.Comment;
            ExpirationDate = BuildExpiration(accessKey.ExpirationTime);
            Permissions = BuildPermissions(accessKey.Permissions);
            State = accessKey.State;
        }


        private static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimit)
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
