using System.Collections.Generic;

namespace HSMServer.Authentication
{
    public interface IUserManager
    {
        User GetUserByCertificateThumbprint(string thumbprint);
        void AddUser(string userName, string certificateThumbprint, string certificateFileName, string password, UserRoleEnum role = UserRoleEnum.DataViewer);
        List<User> Users { get; }
        User GetUserByUserName(string username);

        User Authenticate(string login, string password);
    }
}