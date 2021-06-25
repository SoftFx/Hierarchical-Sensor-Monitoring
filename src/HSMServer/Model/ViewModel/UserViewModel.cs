using HSMServer.Authentication;

namespace HSMServer.Model.ViewModel
{
    public class UserViewModel
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public UserRoleEnum? Role { get; set; }
        public UserViewModel(User user)
        {
            UserId = user.Id.ToString();
            Username = user.UserName;
            Password = user.Password;
            Role = user.Role;
        }
        public UserViewModel()
        {

        }
    }
}
