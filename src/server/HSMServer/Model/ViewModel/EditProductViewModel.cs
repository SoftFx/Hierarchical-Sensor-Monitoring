using HSMServer.Core.Model.Authentication;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        public string ProductName { get; set; }
        public Guid ProductId { get; set; }
        public string EncodedProductId { get; set; }
        public List<KeyValuePair<UserViewModel, ProductRoleEnum>> UsersRights { get; set; }
        public List<AccessKeyViewModel> AccessKeys { get; set; }
        public TelegramSettingsViewModel Telegram { get; set; }

        public EditProductViewModel(ProductNodeViewModel product,
            List<KeyValuePair<User, ProductRoleEnum>> usersRights)
        {
            ProductName = product.Name;
            ProductId = product.Id;
            EncodedProductId = product.EncodedId;
            UsersRights = usersRights.Select(x =>
                new KeyValuePair<UserViewModel, ProductRoleEnum>(new UserViewModel(x.Key), x.Value)).ToList();

            AccessKeys = product.GetEditProductAccessKeys();
            Telegram = product.TelegramSettings;
        }
    }
}
