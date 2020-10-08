using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Authentication;

namespace HSMServer.Extensions
{
    public static class UserExtensions
    {
        public static bool IsSensorAvailable(this User user, string server, string sensor)
        {
            var permissionItem = user.UserPermissions.FirstOrDefault(p => p.ServerName == server);
            return permissionItem != null && permissionItem.IgnoredSensors.Contains(sensor);
        }

        public static bool IsServerAvailable(this User user, string server)
        {
            return user.UserPermissions.FirstOrDefault(p => p.ServerName == server) != null;
        }

        public static IEnumerable<string> GetAvailableServers(this User user)
        {
            return user.UserPermissions.Select(p => p.ServerName);
        }
    }
}
