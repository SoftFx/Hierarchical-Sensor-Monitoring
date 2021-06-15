using HSMServer.Authentication;

namespace HSMServer
{
    public static class UserRoleHelper
    {
        public static bool IsProductCRUDAllow(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                UserRoleEnum.ProductManager => true,
                _ => false
            };
        }

        public static bool IsUsersPageAllow(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                UserRoleEnum.ProductManager => true,
                _ => false
            };
        }

        public static bool IsUserCRUDAllow(UserRoleEnum role)
        {
            return role switch
            {
                UserRoleEnum.Admin => true,
                _ => false
            };
        }
    }
}
