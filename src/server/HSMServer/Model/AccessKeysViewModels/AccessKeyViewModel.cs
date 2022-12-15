using HSMCommon.Constants;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;
using HSMServer.Extensions;

namespace HSMServer.Model.AccessKeysViewModels
{
    public class AccessKeyViewModel
    {
        public Guid Id { get; }

        public ProductNodeViewModel ParentProduct { get; }

        public string AuthorName { get; }

        public string ExpirationDate { get; }


        public KeyState State { get; private set; }

        public string DisplayName { get; private set; }

        public string Permissions { get; private set; }

        public string NodePath { get; private set; }

        public string StatusTitle { get; private set; }


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
            StatusTitle = $"Status : {State}{Environment.NewLine}Expiration date : {ExpirationDate}";
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

        internal static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimited)
                : expirationTime.ToDefaultFormat();

        private static string BuildPermissions(KeyPermissions permissions) =>
            permissions == AccessKeyModel.FullPermissions ? "Full" : permissions.ToString();
    }
}