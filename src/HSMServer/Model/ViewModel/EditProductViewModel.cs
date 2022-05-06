using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public class EditProductViewModel
    {
        public string ProductName { get; set; }
        public string ProductKey { get; set; }
        public List<KeyValuePair<UserViewModel, ProductRoleEnum>> UsersRights { get; set; }

        public EditProductViewModel(ProductModel product, 
            List<KeyValuePair<User, ProductRoleEnum>> usersRights)
        {
            ProductName = product.DisplayName;
            ProductKey = product.Id;
            UsersRights = usersRights.Select(x =>
                new KeyValuePair<UserViewModel, ProductRoleEnum>(
                    new UserViewModel(x.Key), x.Value)).ToList();
        }
    }
}
