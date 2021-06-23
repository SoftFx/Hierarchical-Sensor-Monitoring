using System.Collections.Generic;
using HSMServer.Authentication;

namespace HSMServer.Model.ViewModel
{
    public class UserViewModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public UserRoleEnum? Role { get; set; }
        public UserViewModel(User user)
        {
            Username = user.UserName;
            Password = user.Password;
            Role = user.Role;
        }
        public UserViewModel()
        {

        }
    }
}
