using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class UserViewModel
    {
        public List<KeyValuePair<string, ProductRoleEnum>> ProductsRoles { get;}
        
        public string Username { get;}
        
        public bool IsAdmin { get;}
        
        
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
