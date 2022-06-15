using HSMServer.Core.Cache.Entities;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.AccessKeysViewModels
{
    public class AccessKeyViewModel
    {
        private readonly DateTime _expirationTime;


        public Guid Id { get; }

        public ProductNodeViewModel ParentProduct { get; }

        public string AuthorName { get; }

        public string ExpirationDate { get; }

        public string DisplayName { get; private set; }

        public string Permissions { get; private set; }

        public KeyState State { get; private set; }

        public string NodePath { get; private set; }

        public bool IsChangeAvailable { get; internal set; }

        public bool HasProductColumn { get; internal set; } = true;


        internal AccessKeyViewModel(AccessKeyModel accessKey, ProductNodeViewModel parent, string authorName)
        {
            _expirationTime = accessKey.ExpirationTime;

            Id = accessKey.Id;
            ParentProduct = parent;
            AuthorName = authorName;
            ExpirationDate = BuildExpiration(accessKey.ExpirationTime);

            Update(accessKey);
        }


        internal void Update(AccessKeyModel accessKey)
        {
            DisplayName = accessKey.DisplayName;
            Permissions = BuildPermissions(accessKey.KeyPermissions);
            State = accessKey.KeyState;
        }

        internal void UpdateNodePath()
        {
            var nodePathParts = new List<string>();
            NodeViewModel parent = ParentProduct;

            while (parent != null)
            {
                nodePathParts.Add(parent.Name);
                parent = parent.Parent;
            }

            nodePathParts.Reverse();

            NodePath = string.Join('/', nodePathParts);
        }

        internal AccessKeyViewModel Copy() => (AccessKeyViewModel)MemberwiseClone();

        internal bool HasExpired() => DateTime.UtcNow >= _expirationTime && State < KeyState.Expired;

        internal static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimited)
                : expirationTime.ToString();

        private static string BuildPermissions(KeyPermissions permissions)
        {
            var result = new List<string>(3);

            foreach (var permission in Enum.GetValues(typeof(KeyPermissions)))
                if (permissions.HasFlag((KeyPermissions)permission))
                    result.Add(permission.ToString());

            return string.Join(", ", result);
        }
    }
}
