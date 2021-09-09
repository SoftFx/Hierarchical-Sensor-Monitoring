namespace HSMServer.Constants
{
    public class ViewConstants
    {
        //ToDo: get from configuration
        public const string ApiServer = "https://localhost:44330";
        public const string ApiSwagger = "/api/swagger/index.html";

        public const string HomeController = "Home";
        public const string AccountController = "Account";
        public const string ProductController = "Product";
        public const string AdminController = "Admin";

        public const string AuthenticateAction = "Authenticate";
        public const string LogoutAction = "Logout";
        public const string RegistrationAction = "Registration";

        public const string IndexAction = "Index";
        public const string UpdateAction = "Update";
        public const string UpdateTreeAction = "UpdateTree";
        public const string UpdateInvisibleListsAction = "UpdateInvisibleLists";
        public const string UpdateSelectedListAction = "UpdateSelectedList";
        public const string AddNewSensorsAction = "AddNewSensors";
        public const string HistoryAction = "History";
        public const string RawHistoryAction = "RawHistory";
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

        public const string AddExtraKeyAction = "AddExtraKey";
        public const string AddUserRightAction = "AddUserRight";
        public const string EditUserRoleAction = "EditUserRole";
        public const string RemoveUserRoleAction = "RemoveUserRole";
        public const string RemoveExtraKeyAction = "RemoveExtraKey";
        public const string InviteAction = "Invite";

        public const string SaveConfigObjectAction = "SaveConfigObject";
        public const string SetConfigObjectToDefaultAction = "SetToDefault";

        #region Sensors history

        public const string HistoryHourAction = "HistoryHour";
        public const string HistoryDayAction = "HistoryDay";
        public const string HistoryThreeDaysAction = "HistoryThreeDays";
        public const string HistoryWeekAction = "HistoryWeek";
        public const string HistoryMonthAction = "HistoryMonth";
        public const string HistoryAllAction = "HistoryAll";

        public const string RawHistoryHourAction = "RawHistoryHour";
        public const string RawHistoryDayAction = "RawHistoryDay";
        public const string RawHistoryThreeDaysAction = "RawHistoryThreeDays";
        public const string RawHistoryWeekAction = "RawHistoryWeek";
        public const string RawHistoryMonthAction = "RawHistoryMonth";
        public const string RawHistoryAllAction = "RawHistoryAll";

        public const string ExportHistoryHour = "ExportHistoryHour";
        public const string ExportHistoryDay = "ExportHistoryDay";
        public const string ExportHistoryThreeDays = "ExportHistoryThreeDays";
        public const string ExportHistoryWeek = "ExportHistoryWeek";
        public const string ExportHistoryMonth = "ExportHistoryMonth";
        public const string ExportHistoryAll = "ExportHistoryAll";
        #endregion
    }
}
