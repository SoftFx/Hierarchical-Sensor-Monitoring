using HSMCommon.Constants;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.AccessKeysViewModels
{
    public class AccessKeyViewModel
    {
        public Guid Id { get; }

        public ProductNodeViewModel ParentProduct { get; }

        public string AuthorName { get; }

        public string ExpirationDate { get; }
        
        public string StatusTitle { get; set; }


        public KeyState State { get; private set; }
        
        public string DisplayName { get; private set; }

        public string Permissions { get; private set; }


        public string NodePath { get; private set; }

        public bool HasProductColumn { get; internal set; } = true;


        internal AccessKeyViewModel(AccessKeyModel accessKey, ProductNodeViewModel parent, string authorName)
        {
            Id = accessKey.Id;
            ParentProduct = parent;
            AuthorName = authorName;
            ExpirationDate = BuildExpiration(accessKey.ExpirationTime);
            
            Update(accessKey);
            UpdateNodePath();
        }
        
        internal void Update(AccessKeyModel accessKey)
        {
            DisplayName = accessKey.DisplayName;
            Permissions = BuildPermissions(accessKey.Permissions);
            State = accessKey.State;
            StatusTitle = $"Status : {State}\nExpiration date : {ExpirationDate}";
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

            NodePath = string.Join(CommonConstants.SensorPathSeparator, nodePathParts);
        }

        internal AccessKeyViewModel Copy() => (AccessKeyViewModel)MemberwiseClone();

        internal static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimited)
                : expirationTime.ToString();

        private static string BuildPermissions(KeyPermissions permissions)
        {
            var result = new List<string>(3);

            foreach (var permission in Enum.GetValues<KeyPermissions>())
                if (permissions.HasFlag(permission))
                    result.Add(permission.ToString());

            return string.Join(", ", result);
        }
    }
}
