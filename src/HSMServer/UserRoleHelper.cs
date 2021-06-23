using HSMServer.Authentication;

namespace HSMServer
{
    public static class UserRoleHelper
    {
        public static bool IsProductCRUDAllowed(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }

        public static bool IsUsersPageAllowed(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }

        public static bool IsUserCRUDAllowed(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }

        public static bool IsAllProductsTreeAllowed(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }

        public static bool IsAllSensorsAllowed(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }
    }
}
