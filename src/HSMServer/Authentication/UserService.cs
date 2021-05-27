using System.Linq;
using HSMServer.Extensions;

namespace HSMServer.Authentication
{
    internal class UserService : IUserService
    {
        private readonly IUserManager _userManager;
        public UserService(IUserManager userManager)
        {
            _userManager = userManager;
        }


        public User Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);
            var existingUser = _userManager.Users.SingleOrDefault(u => u.UserName.Equals(login) && !string.IsNullOrEmpty(u.Password) && u.Password.Equals(passwordHash));
            //var existingUser = _userManager.Users.SingleOrDefault(u => u.UserName.Equals(login));

            return existingUser?.WithoutPassword();
        }
    }
}