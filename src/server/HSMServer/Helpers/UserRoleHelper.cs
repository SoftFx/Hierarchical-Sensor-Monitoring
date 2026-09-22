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

        public static bool IsChatsPageAllowed(User user)
        {
            return user.IsAdmin;
        }

        // Alert schedules are global cross-folder objects and Remove triggers
        // a tree-wide policy rewrite (#1409) — the page is admin-only, matching
        // the [AuthorizeIsAdmin] on AlertSchedulesController.
        public static bool IsAlertSchedulesPageAllowed(User user)
        {
            return user.IsAdmin;
        }

        public static bool IsManager(User user)
        {
            return user.ProductsRoles.Any(x => x.Item2 == ProductRoleEnum.ProductManager);
        }
    }
}
