using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Helpers
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
    }
}
