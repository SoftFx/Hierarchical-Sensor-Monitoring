using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        private readonly List<UserViewModel> _usedUsers;



        public List<AccessKeyViewModel> AccessKeys { get; }

        public List<(UserViewModel, ProductRoleEnum)> UsersRights { get; }

        public HashSet<UserViewModel> NotAdminUsers { get; }

        public string EncodedProductId { get; }

        public string ProductName { get; }

        public Guid ProductId { get; }


        public EditProductViewModel(ProductNodeViewModel product,
                                    List<(User, ProductRoleEnum)> usersRights,
                                    List<User> notAdminUsers)
        {
            ProductId = product.Id;
            ProductName = product.Name;
            EncodedProductId = product.EncodedId;

            UsersRights = usersRights.Select(x => (new UserViewModel(x.Item1), x.Item2)).ToList();
            AccessKeys = product.GetAccessKeys();

            _usedUsers = UsersRights.Select(ur => ur.Item1).ToList();
            NotAdminUsers = notAdminUsers.Select(x => new UserViewModel(x)).ToHashSet();
            NotAdminUsers.ExceptWith(_usedUsers);
        }
    }
}