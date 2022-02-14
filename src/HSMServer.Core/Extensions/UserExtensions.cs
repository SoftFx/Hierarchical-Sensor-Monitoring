using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Extensions
{
    internal static class UserExtensions
    {
        public static bool IsSensorAvailable(this User user, string key)
        {
            return ProductRoleHelper.IsAvailable(key, user.ProductsRoles);
        }

        public static bool IsSame(this User user, User comparedUser)
        {
            if (user == null && comparedUser == null)
            {
                return true;
            }

            if (user == null || comparedUser == null)
            {
                return false;
            }

            
            return user.Id == comparedUser.Id;
        }

        public static User WithoutPassword(this User user)
        {
            User copy = new User();
            copy.UserName = user.UserName;
            copy.Password = null;
            copy.CertificateFileName = user.CertificateFileName;
            copy.CertificateThumbprint = user.CertificateThumbprint;
            copy.IsAdmin = user.IsAdmin;
            copy.ProductsRoles = user.ProductsRoles;

            return copy;
        }
    }
}
