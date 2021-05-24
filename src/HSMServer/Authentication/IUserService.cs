using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public interface IUserService
    {
        User Authenticate(string login, string password);
    }
}