using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Html;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class ViewHelper
    {
        #region Product

        public static HtmlString CreateProductList(ClaimsPrincipal claims, List<ProductViewModel> products)
        {
            var user = claims as User;

            return new HtmlString(TableHelper.CreateTable(user, products));
        }

        public static HtmlString CreateUsersRightsTable(ClaimsPrincipal claims, EditProductViewModel model, object users)
        {
            var user = claims as User;
            var notAdminUsers = users as List<User>;

            StringBuilder result = new StringBuilder();
            result.Append(TableHelper.CreateTable(model.ProductName, user, model.UsersRights, notAdminUsers));

            return new HtmlString(result.ToString());
        }

        public static HtmlString CreateProductRoleSelect()
        {
            StringBuilder result = new StringBuilder();

            result.Append("<select style='width: 300px' class='form-select' id='productRole'>");

            foreach (ProductRoleEnum role in Enum.GetValues(typeof(ProductRoleEnum)))
                result.Append($"<option value='{(int)role}'>{role}</option>");

            result.Append("</select>");

            return new HtmlString(result.ToString());
        }
        #endregion

        public static HtmlString CreateUserList(ClaimsPrincipal claims, List<UserViewModel> users, object products)
        {
            var user = claims as User;
            var productsDict = products as Dictionary<string, string>;

            return new HtmlString(TableHelper.CreateTable(user, users, productsDict));
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
