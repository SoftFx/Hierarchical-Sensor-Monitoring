using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Extensions
{
    internal static class UserExtensions
    {
        public static User WithoutPassword(this User user)
        {
            User copy = new User();
            copy.UserName = user.UserName;
            copy.Password = null;
            copy.CertificateFileName = user.CertificateFileName;
            copy.CertificateThumbprint = user.CertificateThumbprint;
            copy.IsAdmin = user.IsAdmin;
            copy.ProductsRoles = user.ProductsRoles;
            copy.Notifications = new(user.Notifications.ToEntity());

            return copy;
        }
    }
}
