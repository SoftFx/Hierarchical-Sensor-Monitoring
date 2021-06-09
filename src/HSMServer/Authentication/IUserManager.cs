using System.Collections.Generic;

namespace HSMServer.Authentication
{
    public interface IUserManager
    {
        User GetUserByCertificateThumbprint(string thumbprint);
        /// <summary>
        /// Add new user with the specified parameters
        /// </summary>
        /// <param name="userName">Login of the new user, must be unique and not empty</param>
        /// <param name="certificateThumbprint">Can be empty for website users</param>
        /// <param name="certificateFileName">Must end with .crt (certificate files extension), can be empty for website users</param>
        /// <param name="passwordHash">Password hash computed with HashComputer.ComputePasswordHash().</param>
        /// <param name="role">UserRoleEnum value, defaults to the role with least rights.</param>
        void AddUser(string userName, string certificateThumbprint, string certificateFileName, string passwordHash, UserRoleEnum role = UserRoleEnum.DataViewer);
        List<User> Users { get; }
        User GetUserByUserName(string username);
        User Authenticate(string login, string password);
        /// <summary>
        /// New user object
        /// </summary>
        /// <param name="user">User object (password field must be password hash).</param>
        void UpdateUser(User user);
    }
}