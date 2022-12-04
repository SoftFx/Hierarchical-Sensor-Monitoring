using HSMServer.Core.Model.Authentication;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.TreeViewModels;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        public string ProductName { get; set; }
        public string ProductId { get; set; }
        public string EncodedProductId { get; set; }
        public List<(UserViewModel, ProductRoleEnum)> UsersRights { get; set; }
        public List<AccessKeyViewModel> AccessKeys { get; set; }
        public TelegramSettingsViewModel Telegram { get; set; }
        public List<User> NotAdminUsers { get; set; }
        public IEnumerable<UserViewModel> UsedUsers { get; set; }

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
            UsedUsers = UsersRights.Select(ur => ur.Item1);
            NotAdminUsers = notAdminUsers;
            RemovedUsedUsers(NotAdminUsers, UsedUsers);
        }

        private void RemovedUsedUsers(List<User> users, IEnumerable<UserViewModel> usedUsers)
        {
            if (users == null || users.Count == 0)
                return;

            if (usedUsers == null || Equals(usedUsers, Enumerable.Empty<UserViewModel>()))
                return;

            foreach (var usedUser in usedUsers)
            {
                var user = users.FirstOrDefault(u => u.UserName.Equals(usedUser.Username));
                if (user != null)
                    users.Remove(user);
            }
        }
    }
}