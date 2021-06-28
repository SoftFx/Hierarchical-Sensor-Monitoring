using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Html;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using HSMSensorDataObjects;

namespace HSMServer.HtmlHelpers
{
    public static class ViewHelper
    {
        public static HtmlString CreateTreeWithLists(TreeViewModel model)
        {
            StringBuilder result = new StringBuilder();

            result.Append(TreeHelper.CreateTree(model));
            result.Append(ListHelper.CreateFullLists(model));

            return new HtmlString(result.ToString());
        }

        public static HtmlString CreateProductList(ClaimsPrincipal claims, List<ProductViewModel> products)
        {
            var user = claims as User;

            return new HtmlString(TableHelper.CreateTable(user, products));
        }

        public static HtmlString CreateUserList(ClaimsPrincipal claims, List<UserViewModel> users)
        {
            var user = claims as User;

            return new HtmlString(TableHelper.CreateTable(user, users));
        }

        public static HtmlString CreateEditProductTables(ClaimsPrincipal claims,
            EditProductViewModel model)
        {
            var user = claims as User;

            StringBuilder result = new StringBuilder();

            result.Append(TableHelper.CreateTable(model.ProductName, user, model.UsersRights));
            result.Append(TableHelper.CreateTable(model.ProductName, user, model.ExtraKeys));
            
            return new HtmlString(result.ToString());
        }

        public static string GetStatusHeaderColorClass(SensorStatus status)
        {
            switch (status)
            {
                case SensorStatus.Unknown:
                    return "tree-icon-unknown";
                case SensorStatus.Ok:
                    return "tree-icon-ok";
                case SensorStatus.Warning:
                    return "tree-icon-warning";
                case SensorStatus.Error:
                    return "tree-icon-error";
                default:
                    return "tree-icon-unknown";
            }
        }
    }
}
