using System.Linq;
using System.Threading.Tasks;
using HSMServer.Extensions;

namespace HSMServer.Authentication
{
    internal class UserService : IUserService
    {
        private readonly UserManager _userManager;
        public UserService(UserManager userManager)
        {
            _userManager = userManager;
        }


        public async Task<User> Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);
            var existingUser =
                await Task.Run(() => _userManager.Users.SingleOrDefault(u => u.UserName.Equals(login) && u.Password.Equals(passwordHash)));

            return existingUser.WithoutPassword();
        }
    }
}