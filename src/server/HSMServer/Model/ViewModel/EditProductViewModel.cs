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
        #region Get Properties
        public string ProductName { get; }
        
        public string ProductId { get; }
        
        public string EncodedProductId { get; }
        
        public List<AccessKeyViewModel> AccessKeys { get; }
        
        public ISet<UserViewModel> NotAdminUsers { get; }
        
        public ISet<UserViewModel> UsedUsers { get;}
        
        #endregion

        #region Set Properties
        
        public TelegramSettingsViewModel Telegram { get; set; }

        public List<(UserViewModel, ProductRoleEnum)> UsersRights { get; set; }
        
        #endregion

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
            
            UsedUsers = UsersRights.Select(ur => ur.Item1).ToImmutableHashSet();
            NotAdminUsers = notAdminUsers.Select(x => new UserViewModel(x)).ToHashSet();
            RemoveUsedUsers(NotAdminUsers, UsedUsers);
        }
        
        private static void RemoveUsedUsers(ISet<UserViewModel> users, IEnumerable<UserViewModel> usedUsers)
        {
            users.ExceptWith(usedUsers);
        }
    }
}