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

        public const string AuthenticateAction = "Authenticate";
        public const string LogoutAction = "Logout";
        public const string RegistrationAction = "Registration";

        public const string IndexAction = "Index";
        public const string UpdateAction = "Update";
        public const string UpdateInvisibleListsAction = "UpdateInvisibleLists";
        public const string UpdateSelectedListAction = "UpdateSelectedList";
        public const string AddNewSensorsAction = "AddNewSensors";

        public const string GetSensorInfoAction = "GetSensorInfo";
        public const string UpdateSensorInfoAction = "UpdateSensorInfo";

        public const string SelectNodeAction = "SelectNode";
        public const string RefreshTreeAction = "RefreshTree";

        public const string RemoveProductAction = "RemoveProduct";
        public const string RemoveNodeAction = "RemoveNode";
        public const string RemoveSensorAction = "RemoveSensor";
        public const string RemoveSensorsAction = "RemoveSensors";
        public const string CreateProductAction = "CreateProduct";
        public const string EditProductAction = "EditProduct";
        public const string GetFileAction = "GetFile";
        public const string GetFileStreamAction = "GetFileStream";
        public const string ProductsAction = "Products";
        public const string UsersAction = "Users";
        public const string CreateUserAction = "CreateUser";
        public const string RemoveUserAction = "RemoveUser";
        public const string UpdateUserAction = "UpdateUser";

        public const string AddExtraKeyAction = "AddExtraKey";
        public const string AddUserRightAction = "AddUserRight";
        public const string EditUserRoleAction = "EditUserRole";
        public const string RemoveUserRoleAction = "RemoveUserRole";
        public const string RemoveExtraKeyAction = "RemoveExtraKey";
        public const string InviteAction = "Invite";

        public const string SaveConfigObjectAction = "SaveConfigObject";
        public const string SetConfigObjectToDefaultAction = "SetToDefault";

        public const string NodeUpdateTimeFormat = "dd/MM/yyyy HH:mm:ss";

        #region Sensors history

        public const string HistoryAction = "History";
        public const string HistoryAllAction = "HistoryAll";

        public const string RawHistoryAction = "RawHistory";
        public const string RawHistoryAllAction = "RawHistoryAll";

        public const string RawHistoryLatestAction = "RawHistoryLatest";
        public const string HistoryLatestAction = "HistoryLatest";

        public const string ExportHistoryAction = "ExportHistory";
        public const string ExportHistoryAllAction = "ExportHistoryAll";

        #endregion
    }
}
