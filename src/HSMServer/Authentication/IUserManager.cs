using System.Collections.Generic;

namespace HSMServer.Authentication
{
    public interface IUserManager
    {
        User GetUserByCertificateThumbprint(string thumbprint);
        void AddNewUser(string userName, string certificateThumbprint, string certificateFileName, string password, string role = "");
        List<User> Users { get; }
        User GetUserByUserName(string username);
    }
}