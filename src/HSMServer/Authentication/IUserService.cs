using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public interface IUserService
    {
        Task<User> Authenticate(string login, string password);
    }
}