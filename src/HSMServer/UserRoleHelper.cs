using HSMServer.Authentication;

namespace HSMServer
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

        public static bool IsAdminPageAllowed(User user)
        {
            return user.IsAdmin;
        }
    }
}
