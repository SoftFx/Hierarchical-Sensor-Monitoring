using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public interface IUserManager
    {
        User this[Guid id] { get; }

        User this[string name] { get; }

        event Action<User> Added;
        event Action<User> Updated;
        event Action<User> Removed;

        /// <summary>
        /// Add new user with the specified parameters
        /// </summary>
        /// <param name="userName">Login of the new user, must be unique and not empty</param>
        /// <param name="passwordHash">Password hash computed with HashComputer.ComputePasswordHash().</param>
        Task<bool> AddUser(string userName, string passwordHash, bool isAdmin, List<(Guid, ProductRoleEnum)> productRoles = null);

        Task<bool> TryAdd(User user);

        Task<bool> TryUpdate(UserUpdate update);

        /// <summary>
        /// New user object
        /// </summary>
        /// <param name="user">User object (password field must be password hash).</param>
        Task<bool> UpdateUser(User user);

        /// <summary>
        /// Remove user with the specified userName
        /// </summary>
        /// <param name="userName">Name of the user to remove.</param>
        Task RemoveUser(string userName);

        bool TryAuthenticate(string login, string password);

        bool TryGetIdByName(string name, out Guid id);

        List<User> GetViewers(Guid productId);
        List<User> GetManagers(Guid productId);
        IEnumerable<User> GetUsers(Func<User, bool> filter = null);

        Task InitializeUsers();
    }
}