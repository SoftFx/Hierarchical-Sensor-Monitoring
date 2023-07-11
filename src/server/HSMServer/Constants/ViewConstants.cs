namespace HSMServer.Constants
{
    public static class ViewConstants
    {
        //ToDo: get from configuration
        public const string ApiSwagger = "/api/swagger/index.html";

        public const string HomeController = "Home";
        public const string AccountController = "Account";
        public const string ProductController = "Product";
        public const string FoldersController = "Folders";
        public const string ConfigurationController = "Configuration";
        public const string AccessKeysController = "AccessKeys";
        public const string NotificationsController = "Notifications";
        public const string HistoryController = "SensorHistory";

        public const string LogoutAction = "Logout";
        public const string RegistrationAction = "Registration";

        public const string IndexAction = "Index";
        
        public const string RemoveProductAction = "RemoveProduct";
        public const string EditProductAction = "EditProduct";

        public const string UsersAction = "Users";
        public const string CreateUserAction = "CreateUser";
        public const string RemoveUserAction = "RemoveUser";
        public const string UpdateUserAction = "UpdateUser";

        public const string AddUserRightAction = "AddUserRight";
        public const string EditUserRoleAction = "EditUserRole";
        public const string RemoveUserRoleAction = "RemoveUserRole";
        public const string InviteAction = "Invite";
    }
}
