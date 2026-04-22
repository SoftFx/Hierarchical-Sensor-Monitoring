using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public interface IUserManager : IConcurrentStorageNames<User, UserEntity, UserUpdate>
    {
        Task<bool> AddUser(string userName, string passwordHash, bool isAdmin, List<(Guid, ProductRoleEnum)> productRoles = null);

        Task<bool> TryAdd(User user);

        Task<bool> UpdateUser(User user);

        bool TryAuthenticate(string login, string password);

        bool TryGetIdByName(string name, out Guid id);


        List<User> GetViewers(Guid productId);

        List<User> GetManagers(Guid productId);

        IEnumerable<User> GetUsers(Func<User, bool> filter = null);

        McpAccessKeyModel GetMcpAccessKey(Guid keyId);

        IEnumerable<McpAccessKeyModel> GetUserMcpAccessKeys(Guid userId);

        bool AddMcpAccessKey(McpAccessKeyModel key);

        bool UpdateMcpAccessKey(McpAccessKeyModel key);

        bool RemoveMcpAccessKey(Guid keyId);
    }
}