using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Authentication
{
    public interface IUserManager
    {
        public event Action<User> UpdateUserEvent;
        public event Action<User> RemoveUserEvent;

        /// <summary>
        /// Add new user with the specified parameters
        /// </summary>
        /// <param name="userName">Login of the new user, must be unique and not empty</param>
        /// <param name="passwordHash">Password hash computed with HashComputer.ComputePasswordHash().</param>
        void AddUser(string userName, string passwordHash, bool isAdmin, List<KeyValuePair<string, ProductRoleEnum>> productRoles = null);
        public void AddUser(User user);

        /// <summary>
        /// New user object
        /// </summary>
        /// <param name="user">User object (password field must be password hash).</param>
        void UpdateUser(User user);

        /// <summary>
        /// Remove user with the specified userName
        /// </summary>
        /// <param name="userName">Name of the user to remove.</param>
        void RemoveUser(string userName);

        User Authenticate(string login, string password);

        User GetCopyUser(Guid id);
        User GetUser(Guid id);
        User GetUserByUserName(string username);

        List<User> GetViewers(Guid productId);
        List<User> GetManagers(Guid productId);
        IEnumerable<User> GetUsers(Func<User, bool> filter = null);
    }
}