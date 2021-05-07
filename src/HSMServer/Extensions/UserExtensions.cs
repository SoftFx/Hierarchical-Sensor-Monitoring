using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;

namespace HSMServer.Extensions
{
    internal static class UserExtensions
    {
        public static bool IsSensorAvailable(this User user, string server, string sensor)
        {
            var permissionItem = user.UserPermissions.FirstOrDefault(p => p.ProductName == server);
            return permissionItem != null && permissionItem.IgnoredSensors.Contains(sensor);
        }

        public static bool IsProductAvailable(this User user, string server)
        {
            return user.UserPermissions.FirstOrDefault(p => p.ProductName == server) != null;
        }

        public static IEnumerable<string> GetAvailableServers(this User user)
        {
            return user.UserPermissions.Select(p => p.ProductName);
        }

        public static bool IsSame(this User user, User user2)
        {
            if (user == null && user2 == null)
            {
                return true;
            }

            if (user == null || user2 == null)
            {
                return false;
            }

            return user.CertificateThumbprint.Equals(user2.CertificateThumbprint) &&
                   user.UserName.Equals(user2.UserName);
        }
    }
}
