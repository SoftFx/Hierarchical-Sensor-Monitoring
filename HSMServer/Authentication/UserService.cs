using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public interface IUserService
    {
        Task<User> Authenticate(string username, string password);
        Task<IEnumerable<User>> GetAll();
        bool IsAuthorized(string username, string password);
    }
    public class UserService : IUserService
    {
        private List<User> _users = new List<User>
        {
            new User { Username = "Admin", Password = "Admin", Role = "Admin"},
            new User { Username = "adnf", Password = "bnfllasdn", Role = "aspifdofk"}
        };

        public async Task<User> Authenticate(string username, string password)
        {
            var user = await Task.Run(() => _users.SingleOrDefault(u => u.Username == username && u.Password == password));

            return user?.WithoutPassword();
        }

        public async Task<IEnumerable<User>> GetAll()
        {
            return await Task.Run(() => _users.WithoutPasswords());
        }

        public bool IsAuthorized(string username, string password)
        {
            var user = _users.SingleOrDefault(u => u.Username == username && u.Password == password);
            return user != null;
        }
    }
}
