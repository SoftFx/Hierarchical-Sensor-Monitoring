using System.Linq;
using HSMServer.Extensions;
using Microsoft.Extensions.WebEncoders.Testing;

namespace HSMServer.Authentication
{
    internal class UserService : IUserService
    {
        private readonly IUserManager _userManager;
        public UserService(IUserManager userManager)
        {
            _userManager = userManager;
            _userManager.Users.Add(new User() {UserName = "admin", Password = HashComputer.ComputePasswordHash("admin")});
        }


        public User Authenticate(string login, string password)
        {
            //var passwordHash = HashComputer.ComputePasswordHash(password);
            //var existingUser = _userManager.Users.SingleOrDefault(u => u.UserName.Equals(login) && !string.IsNullOrEmpty(u.Password) && u.Password.Equals(passwordHash));

            //return existingUser?.WithoutPassword();
            return new User() {UserName = login, Password = password}.WithoutPassword();
        }
    }
}