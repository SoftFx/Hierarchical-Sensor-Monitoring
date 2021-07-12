namespace HSMServer
{
    public static class UserRoleHelper
    {
        public static bool IsProductCRUDAllowed(bool isAdmin)
        {
            return isAdmin switch
            {
                true => true,
                _ => false
            };
        }

        public static bool IsUsersPageAllowed(bool isAdmin)
        {
            return isAdmin switch
            {
                true => true,
                _ => false
            };
        }

        public static bool IsUserCRUDAllowed(bool isAdmin)
        {
            return isAdmin switch
            {
                true => true,
                _ => false
            };
        }

        public static bool IsAllProductsTreeAllowed(bool isAdmin)
        {
            return isAdmin switch
            {
                true => true,
                _ => false
            };
        }

        public static bool IsAllSensorsAllowed(bool isAdmin)
        {
            return isAdmin switch
            {
                true => true,
                _ => false
            };
        }
    }
}
