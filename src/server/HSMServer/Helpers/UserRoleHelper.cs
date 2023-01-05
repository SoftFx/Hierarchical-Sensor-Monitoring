using HSMServer.Model.Authentication;
using System.Linq;

namespace HSMServer.Helpers
{
    public static class UserRoleHelper
    {
        public static bool IsProductCRUDAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsUsersPageAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsUserCRUDAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsAllProductsTreeAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsAllSensorsAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsConfigurationPageAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsManager(User user)
        {
            return user.ProductsRoles.Any(x => x.Value == ProductRoleEnum.ProductManager);
        }
    }
}
