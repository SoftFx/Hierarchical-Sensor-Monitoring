using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Authentication
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
        void AddUser(string userName, string certificateThumbprint, string certificateFileName,
            string passwordHash, bool isAdmin, List<KeyValuePair<string, ProductRoleEnum>> productRoles = null);
        List<User> Users { get; }
        User GetUser(Guid id);
        User GetUserByUserName(string username);
        User Authenticate(string login, string password);
        /// <summary>
        /// New user object
        /// </summary>
        /// <param name="user">User object (password field must be password hash).</param>
        void UpdateUser(User user);
        /// <summary>
        /// Removes user 
        /// </summary>
        /// <param name="user"></param>
        void RemoveUser(User user);
        /// <summary>
        /// Remove user with the specified userName
        /// </summary>
        /// <param name="userName">Name of the user to remove.</param>
        void RemoveUser(string userName);
        /// <summary>
        /// Get users from (page - 1) * pageSize to page * pageSize
        /// </summary>
        /// <param name="page">Page number, must not be less than zero</param>
        /// <param name="pageSize">Page size, must not be less than zero</param>
        /// <returns>Users list if the amount of users is bigger than (page - 1) * pageSize, empty list otherwise</returns>
        List<User> GetUsersPage(int page = 1, int pageSize = 1);
        List<User> GetViewers(string productKey);
        List<User> GetAllViewers(string productKey);
        List<User> GetManagers(string productKey);
        List<User> GetAllManagers();
        List<User> GetUsersNotAdmin();
    }
}