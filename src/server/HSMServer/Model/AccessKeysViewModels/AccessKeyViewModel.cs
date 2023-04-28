using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using System;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;

namespace HSMServer.Model.AccessKeysViewModels
{
    public sealed class AccessKeyViewModel
    {
        public Guid Id { get; }

        public ProductNodeViewModel ParentProduct { get; }
        
        public User Author { get; }

        public string ExpirationDate { get; }


        public KeyState State { get; private set; }

        public string DisplayName { get; private set; }

        public string Permissions { get; private set; }

        public string NodePath => ParentProduct?.FullPath;

        public string StatusTitle { get; private set; }


        internal AccessKeyViewModel(AccessKeyModel accessKey, ProductNodeViewModel parent, User author)
        {
            Id = accessKey.Id;
            ParentProduct = parent;
            Author = author;
            ExpirationDate = BuildExpiration(accessKey.ExpirationTime);

            Update(accessKey);
        }

        internal void Update(AccessKeyModel accessKey)
        {
            DisplayName = accessKey.DisplayName;
            Permissions = BuildPermissions(accessKey.Permissions);
            State = accessKey.State;
            StatusTitle = $"Status : {State}{Environment.NewLine}Expiration date : {ExpirationDate}";
        }

        internal static string BuildExpiration(DateTime expirationTime) =>
            expirationTime == DateTime.MaxValue
                ? nameof(AccessKeyExpiration.Unlimited)
                : expirationTime.ToDefaultFormat();

        private static string BuildPermissions(KeyPermissions permissions) =>
            permissions == AccessKeyModel.FullPermissions ? "Full" : permissions.ToString();
    }
}