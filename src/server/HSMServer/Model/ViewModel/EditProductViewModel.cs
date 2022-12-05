using HSMServer.Core.Model.Authentication;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        private readonly List<UserViewModel> _usedUsers;
        
        
        public string ProductName { get; }

        public string ProductId { get; }

        public string EncodedProductId { get; }

        public List<AccessKeyViewModel> AccessKeys { get; }

        public HashSet<UserViewModel> NotAdminUsers { get; }

        public TelegramSettingsViewModel Telegram { get; }

        public List<(UserViewModel, ProductRoleEnum)> UsersRights { get; }
       
        
        public EditProductViewModel(ProductNodeViewModel product,
                                    List<(User, ProductRoleEnum)> usersRights,
                                    List<User> notAdminUsers)
        {
            ProductName = product.Name;
            ProductId = product.Id;
            EncodedProductId = product.EncodedId;

            UsersRights = usersRights.Select(x => (new UserViewModel(x.Item1), x.Item2)).ToList();
            AccessKeys = product.GetEditProductAccessKeys();
            Telegram = product.TelegramSettings;

            _usedUsers = UsersRights.Select(ur => ur.Item1).ToList();
            NotAdminUsers = notAdminUsers.Select(x => new UserViewModel(x))
                .ToHashSet();
            NotAdminUsers.ExceptWith(_usedUsers);
        }
    }
}