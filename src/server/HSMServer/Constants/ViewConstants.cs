namespace HSMServer.Constants
{
    public static class ViewConstants
    {
        //ToDo: get from configuration
        public const string ApiServer = "https://localhost:44330";
        public const string ApiSwagger = "/api/swagger/index.html";

        public const string HomeController = "Home";
        public const string AccountController = "Account";
        public const string ProductController = "Product";
        public const string ConfigurationController = "Configuration";
        public const string AccessKeysController = "AccessKeys";
        public const string NotificationsController = "Notifications";

        public const string AuthenticateAction = "Authenticate";
        public const string LogoutAction = "Logout";
        public const string RegistrationAction = "Registration";

        public const string IndexAction = "Index";
        public const string UpdateAction = "Update";
        public const string UpdateSelectedNodeAction = "UpdateSelectedNode";

        public const string GetSensorInfoAction = "GetSensorInfo";
        public const string UpdateSensorInfoAction = "UpdateSensorInfo";

        public const string SelectNodeAction = "SelectNode";
        public const string RefreshTreeAction = "RefreshTree";
        public const string RemoveNodeAction = "RemoveNode";

        public const string RemoveProductAction = "RemoveProduct";
        public const string CreateProductAction = "CreateProduct";
        public const string EditProductAction = "EditProduct";
        public const string GetFileAction = "GetFile";
        public const string GetFileStreamAction = "GetFileStream";
        public const string ProductsAction = "Products";
        public const string UsersAction = "Users";
        public const string CreateUserAction = "CreateUser";
        public const string RemoveUserAction = "RemoveUser";
        public const string UpdateUserAction = "UpdateUser";

        public const string AddUserRightAction = "AddUserRight";
        public const string EditUserRoleAction = "EditUserRole";
        public const string RemoveUserRoleAction = "RemoveUserRole";
        public const string InviteAction = "Invite";

        #region Sensors history

        public const string HistoryAction = "History";
        public const string RawHistoryAction = "RawHistory";
        public const string ExportHistoryAction = "ExportHistory";

        #endregion
    }
}
