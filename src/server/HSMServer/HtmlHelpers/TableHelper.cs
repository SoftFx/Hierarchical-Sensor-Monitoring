using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class TableHelper
    {
        #region [ Users ]
        public static string CreateTable(User user, List<UserViewModel> users, Dictionary<string, string> products)
        {
            StringBuilder result = new StringBuilder();

            result.Append("<div style='margin: 10px'>" +
                "<div class='row justify-content-start'><div class='col-2'>" +
                "<h5 style='margin: 10px 20px 10px;'>Users</h5></div></div></div>");

            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'><thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Username</th>" +
                "<th scope='col'>Password</th>" +
                "<th dcope='col'>IsAdmin</th>" +
                "<th scope='col'>Products</th>" +
                "<th scope='col'>Action</th>");

            result.Append("<tbody>");

            if (users == null || users.Count == 0) return result.ToString();

            //create 
            if (UserRoleHelper.IsUserCRUDAllowed(user))
            {
                result.Append("<tr><th>0</th>" +
                "<th><input id='createName' type='text' class='form-control'/></th>" +
                "<th><input id='createPassword' type='password' class='form-control'/></th>" +
                "<th><input id='createIsAdmin' type='checkbox' class='form-check-input' style='margin-left: 0px'/></th>" +
                "<th>---</th>" +
                "<th><button id='createButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                $"<i class='fas fa-plus'></i></button></th></tr>");
            }

            int index = 1;
            foreach (var userItem in users)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{userItem.Username}</td>" +
                    $"<td>**************</td>");

                result.Append($"<td><input id='isAdmin_{userItem.Username}' type='checkbox' class='form-check-input' " +
                    $"disabled value='{userItem.IsAdmin}' style='margin-left: 0px' ");

                if (userItem.IsAdmin)
                    result.Append($"checked");

                result.Append("/></td>");

                result.Append($"<td>{CreateUserProductsList(userItem.ProductsRoles, products)}</td>");

                result.Append("<td style='width: 25%'>");
                if (UserRoleHelper.IsUserCRUDAllowed(user))
                    result.Append($"<button style='margin-left: 5px' id='delete_{userItem.Username}' " +
                        "type='button' class='btn btn-secondary' title='delete'>" +
                        "<i class='fas fa-trash-alt'></i></button>");

                result.Append($"<button style='margin-left: 5px' id='change_{userItem.Username}' " +
                    "type='button' class='btn btn-secondary' title='change'>" +
                    "<i class='fas fa-user-edit'></i></button>" +

                    $"<button disabled style='margin-left: 5px' id='ok_{userItem.Username}' " +
                    $"type='button' class='btn btn-secondary' title='ok'>" +
                    "<i class='fas fa-check'></i></button>" +

                    $"<button disabled style='margin-left: 5px' id='cancel_{userItem.Username}' " +
                    $"type='button' class='btn btn-secondary' title='cancel'>" +
                    "<i class='fas fa-times'></i></button></td></tr>");
                index++;
            }


            result.Append("</tbody></table></div></div>");

            return result.ToString();
        }

        private static string CreateUserProductsList(List<KeyValuePair<string, ProductRoleEnum>> productsRights,
            Dictionary<string, string> products)
        {
            StringBuilder result = new StringBuilder();

            if (productsRights == null || !productsRights.Any())
                return "---";

            foreach (var right in productsRights)
            {
                var name = products?.FirstOrDefault(p => p.Key.Equals(right.Key)).Value;
                result.AppendLine($"{name ?? right.Key} ({right.Value})<br>");
            }

            return result.ToString();
        }

        #endregion

        #region [ Products ]
        public static string CreateTable(User user, List<ProductViewModel> products)
        {
            StringBuilder result = new StringBuilder();
            //header template
            result.Append("<div style='margin: 10px'>" +
                "<div class='row justify-content-start'><div class='col-2'>" +
                "<h5 style='margin: 10px 20px 10px;'>Products</h5></div></div></div>");


            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'><thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Name</th>" +
                "<th scope='col'>Key</th>" +
                "<th scope='col colspan='2'></th>" +
                "<th scope='col'>Creation Date</th>" +
                "<th scope='col'>Manager</th>");

            if (UserRoleHelper.IsProductCRUDAllowed(user)
                || ProductRoleHelper.IsProductActionAllowed(user.ProductsRoles))
                result.Append("<th scope='col'>Action</th>");

            result.Append("</tr></thead><tbody>");

            //create 
            if (UserRoleHelper.IsProductCRUDAllowed(user))
                result.Append("<tr><th>0</th>" +
                    "<th><input id='createName' type='text' class='form-control'/>" +
                    "<span style='display: none;' id='new_product_name_span'></th>" +
                    "<th>---</th>" +
                    "<th></th>" +
                    "<th>---</th>" +
                    "<th>---</th>" +
                    "<th><button id='createButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                    "<i class='fas fa-plus'></i></button></th></tr>");

            if (products == null || products.Count == 0)
            {
                result.Append("</tbody></table></div>");
                return result.ToString();
            }

            int index = 1;
            foreach (var product in products)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{product.Name}</td>" +
                    $"<td id='key_{product.Id}' value='{product.Id}'>{product.Key}</td> " +
                    $"<td><button id='copy_{product.Id}' data-clipboard-text='{product.Key}' title='copy key' type='button' class='btn btn-secondary'>" +
                    "<i class='far fa-copy'></i></button>" +
                    $"<input style='display: none' type='text' id='inputName_{product.Id}' value='{product.Name}'/></td>" +
                    $"<td>{product.CreationDate}</td>" +
                    $"<td>{product.ManagerName}</td>");

                if (UserRoleHelper.IsProductCRUDAllowed(user) ||
                    ProductRoleHelper.IsManager(product.Id, user.ProductsRoles))
                    result.Append($"<td><button style='margin-left: 5px' id='change_{product.EncodedId}' " +
                    "type='button' class='btn btn-secondary' title='edit'>" +
                    "<i class='fas fa-edit'></i></button>");

                if (UserRoleHelper.IsProductCRUDAllowed(user))
                    result.Append($"<button id='delete_{product.Id}' style='margin-left: 5px' " +
                        "type='button' class='btn btn-secondary' title='delete'>" +
                        "<i class='fas fa-trash-alt'></i></button>");


                result.Append("</tr>");
                index++;
            }

            result.Append("</tbody></table></div>");

            return result.ToString();
        }

        #endregion

        #region [ Edit Product: User Right ]

        public static string CreateTable(string productName, User user,
            List<KeyValuePair<UserViewModel, ProductRoleEnum>> usersRights, List<User> notAdminUsers)
        {
            StringBuilder result = new StringBuilder();
            //header template
            result.Append("<div style='margin: 10px'>" +
                "<div class='row justify-content-start'>" +
                $"<h5 style='margin: 10px 20px 10px;'>Edit Product '{productName}' Members</h5></div></div>");

            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'>" +
                "<thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Account</th>" +
                "<th scope='col'>Role</th>" +
                "<th scope='col'>Action</th></tr>");

            result.Append("</thead><tbody>");

            var usedUsers = usersRights.Select(ur => ur.Key)?.ToList();
            //create 
            result.Append("<tr><th>0</th>" +
                    $"<th>{CreateUserSelect(usedUsers, notAdminUsers)}" +
                    $"<span style='display: none;' id='new_user_span'></th>" +
                    $"<th>{CreateProductRoleSelect()}</th>" +
                    "<th><button id='createButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");

            if (usersRights == null || usersRights.Count == 0)
            {
                result.Append("</tbody></table></div>");
                return result.ToString();
            }

            int index = 1;
            foreach (var userRight in usersRights)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{userRight.Key.Username}" +
                    $"<input id='userId_{userRight.Key.Username}' value='{userRight.Key.UserId}' style='display: none'/></td>" +
                    $"<td>{CreateProductRoleSelect(userRight.Key.Username, userRight.Value)}");

                result.Append($"<td><button style='margin-left: 5px' id='change_{userRight.Key.Username}' " +
                    $"type='button' class='btn btn-secondary' title='edit'>" +
                    "<i class='fas fa-edit'></i></button>" +

                    $"<button id='delete_{userRight.Key.Username}' style='margin-left: 5px' " +
                    $"type='button' class='btn btn-secondary' title='delete'>" +
                    $"<i class='fas fa-trash-alt'></i></button>" +

                    $"<button disabled style='margin-left: 5px' id='ok_{userRight.Key.Username}' " +
                    $"type='button' class='btn btn-secondary' title='ok'>" +
                    "<i class='fas fa-check'></i></button>" +

                    $"<button disabled style='margin-left: 5px' id='cancel_{userRight.Key.Username}' " +
                    $"type='button' class='btn btn-secondary' title='cancel'>" +
                    "<i class='fas fa-times'></i></button></td>");

                result.Append("</tr>");
                index++;
            }

            result.Append("</tbody></table></div>");
            return result.ToString();
        }

        private static string CreateUserSelect(List<UserViewModel> usedUsers, List<User> notAdminUsers)
        {
            RemovedUsedUsers(notAdminUsers, usedUsers);

            StringBuilder result = new StringBuilder();

            if (notAdminUsers != null && notAdminUsers.Count != 0)
            {
                result.Append("<select class='form-select' id='createUser'>");

                foreach (var user in notAdminUsers)
                    result.Append($"<option value='{user.Id}'>{user.UserName}</option>");
            }
            else result.Append("<select disabled class='form-select' id='createUser'>");

            result.Append("</select>");

            return result.ToString();
        }

        private static void RemovedUsedUsers(List<User> users, List<UserViewModel> usedUsers)
        {
            if (users == null || !users.Any())
                return;

            if (usedUsers == null || !usedUsers.Any())
                return;

            foreach (var usedUser in usedUsers)
            {
                var user = users.FirstOrDefault(u => u.UserName.Equals(usedUser.Username));
                if (user != null)
                    users.Remove(user);
            }
        }
        private static string CreateProductRoleSelect()
        {
            StringBuilder result = new StringBuilder();

            result.Append("<select class='form-select' id='createProductRole'>");

            foreach (ProductRoleEnum role in Enum.GetValues(typeof(ProductRoleEnum)))
                result.Append($"<option value='{(int)role}'>{role}</option>");

            result.Append("</select>");

            return result.ToString();
        }

        private static string CreateProductRoleSelect(string username, ProductRoleEnum productRole)
        {
            StringBuilder result = new StringBuilder();
            result.Append($"<select class='form-select' disabled id='role_{username}'>");

            foreach (ProductRoleEnum role in Enum.GetValues(typeof(ProductRoleEnum)))
            {
                if (role == productRole)
                    result.Append($"<option selected value='{(int)role}'>{role}</option>");
                else
                    result.Append($"<option value='{(int)role}'>{role}</option>");
            }

            return result.ToString();
        }
        #endregion
    }
}
