namespace HSMServer.Constants
{
    public class ViewConstants
    {
        //ToDo: get from configuration
        public const string ApiServer = "https://localhost:44330";

        public const string HomeController = "Home";
        public const string AccountController = "Account";
        public const string ProductController = "Product";

        public const string AuthenticateAction = "Authenticate";
        public const string LogoutAction = "Logout";

        public const string IndexAction = "Index";
        public const string UpdateAction = "Update";
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

        public const string AddExtraKeyToProductAction = "AddExtraKeyToProduct";
        public const string AddUserRightAction = "AddUserRight";
    }
}
