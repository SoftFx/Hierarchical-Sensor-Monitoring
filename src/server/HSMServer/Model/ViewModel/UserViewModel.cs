using System;
using HSMServer.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class UserViewModel
    {
        public List<(Guid, ProductRoleEnum)> ProductsRoles { get; }


        public bool IsAdmin { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string UserId { get; set; }


        public UserViewModel(User user)
        {
            UserId = user.Id.ToString();
            Username = user.UserName;
            Password = user.Password;
            IsAdmin = user.IsAdmin;
            ProductsRoles = user.ProductsRoles;
        }
        public UserViewModel()
        {

        }

        public override bool Equals(object obj)
        {
            return UserId.Equals((obj as UserViewModel)?.UserId);
        }

        public override int GetHashCode()
        {
            return UserId.GetHashCode();
        }
    }
}
