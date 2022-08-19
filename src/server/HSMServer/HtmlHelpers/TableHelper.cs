using HSMServer.Constants;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HSMServer.HtmlHelpers
{
    public static class TableHelper
    {
        // regex for looking for strings that contain HTML markup
        private static readonly Regex _tagRegex = new(@"<\s*([^ >]+)[^>]*>.*?<\s*/\s*\1\s*>");

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
                    result.Append($"<td><button style='margin-left: 5px' id='change_{product.Id}' " +
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

        #region Sensor history tables

        public static string CreateHistoryTable(List<BaseValue> values, int type, string encodedPath)
        {
            if (values.Count == 0)
                return string.Empty;

            values.Reverse();

            var sb = new StringBuilder(1 << 3);
            sb.Append("<div>");

            switch ((SensorType)type)
            {
                case SensorType.Boolean:
                    sb.Append(CreateTable(values.Select(v => (BooleanValue)v).ToList()));
                    break;
                case SensorType.Integer:
                    sb.Append(CreateTable(values.Select(v => (IntegerValue)v).ToList()));
                    break;
                case SensorType.Double:
                    sb.Append(CreateTable(values.Select(v => (DoubleValue)v).ToList()));
                    break;
                case SensorType.String:
                    sb.Append(CreateTable(values.Select(v => (StringValue)v).ToList()));
                    break;
                case SensorType.IntegerBar:
                    sb.Append(CreateTable(values.Select(v => (IntegerBarValue)v).ToList()));
                    break;
                case SensorType.DoubleBar:
                    sb.Append(CreateTable(values.Select(v => (DoubleBarValue)v).ToList()));
                    break;
                default:
                    break;
            }

            sb.Append($"<input id='oldest_date_{encodedPath}' type='text' style='display: none'" +
                      $" value='{values.LastOrDefault()?.Time.ToUniversalTime().ToString("O") ?? ""}' /></div>");

            return sb.ToString();
        }

        private static string CreateTable(List<BooleanValue> booleanValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th scope='col'>Date</th>");
            sb.Append("<th scope='col'>Value</th>");
            sb.Append("<th scope='col'>Comment</th></tr></thead><tbody>");

            foreach (var value in booleanValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td>{value.Value}</td><td>{GetHistoryRawComment(value.Comment)}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string CreateTable(List<IntegerValue> integerValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th>Date</th>");
            sb.Append("<th scope='col'>Number</th>");
            sb.Append("<th scope='col'>Comment</th></tr></thead><tbody>");

            foreach (var value in integerValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td scope='row'>{value.Value}</td><td>{GetHistoryRawComment(value.Comment)}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string CreateTable(List<DoubleValue> doubleValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th>Date</th>");
            sb.Append("<th scope='col'>Number</th>");
            sb.Append("<th scope='col'>Comment</th></tr></thead><tbody>");

            foreach (var value in doubleValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td scope='row'>{value.Value}</td><td>{GetHistoryRawComment(value.Comment)}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string CreateTable(List<StringValue> stringValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th>Date</th>");
            sb.Append("<th scope='col'>String value</th>");
            sb.Append("<th scope='col'>Comment</th></tr></thead><tbody>");

            foreach (var value in stringValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td scope='row'>{value.Value}</td><td>{GetHistoryRawComment(value.Comment)}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string CreateTable(List<IntegerBarValue> intBarValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th>Date</th>");
            sb.Append("<th scope='col'>Min</th>");
            sb.Append("<th scope='col'>Mean</th>");
            sb.Append("<th scope='col'>Max</th></tr></thead><tbody>");

            foreach (var value in intBarValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td scope='row'>{value.Min}</td><td>{value.Mean}</td><td>{value.Max}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string CreateTable(List<DoubleBarValue> doubleBarValues)
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append("<table class='table table-striped'><thead><tr>");
            sb.Append("<th>Date</th>");
            sb.Append("<th scope='col'>Min</th>");
            sb.Append("<th scope='col'>Mean</th>");
            sb.Append("<th scope='col'>Max</th></tr></thead><tbody>");

            foreach (var value in doubleBarValues)
                sb.Append($"<tr><td>{value.Time.ToString(ViewConstants.NodeUpdateTimeFormat)}</td><td scope='row'>{value.Min}</td><td>{value.Mean}</td><td>{value.Max}</td></tr>");

            sb.Append("</tbody>");

            return sb.ToString();
        }

        private static string GetHistoryRawComment(string comment) =>
            !string.IsNullOrEmpty(comment) && _tagRegex.IsMatch(comment) ? "This comment is invalid" : comment;

        #endregion

        #region Sensor info

        public static string CreateSensorInfoTable(SensorInfoViewModel sensorInfo)
        {
            StringBuilder result = new StringBuilder();

            string encodedId = SensorPathHelper.EncodeGuid(sensorInfo.Id);
            result.Append("<div style='margin: 10px'><div class='row justify-content-start'><div class='col-md-auto'>" +
                          $"<h5 style='margin: 10px 20px 10px;'>{sensorInfo.ProductName}/{sensorInfo.Path}</h5><div>" +
                          $"{CreateEditButtonForInfo(encodedId)}{CreateSaveButtonForInfo(encodedId)}" +
                          $"{CreateResetButtonForInfo(encodedId)}</div></div></div></div>");
            result.Append("<table class='table table-bordered'><tbody>");
            result.Append($"<tr><td>Product</td><td>{sensorInfo.ProductName}</td></tr>");
            result.Append($"<tr><td>Path</td><td>{sensorInfo.Path}</td></tr>");
            result.Append($"<tr><td>Sensor type</td><td>{sensorInfo.SensorType}</td></tr>");
            result.Append("<tr><td>Expected update interval<i class='fas fa-question-circle' " +
                          "title='Time format: dd.hh:mm:ss min value 00:01:00'></i></td><td><input disabled type='text' " +
                          $"class='form-control' style='max-width:300px' id='interval_{encodedId}' " +
                          $"value='{sensorInfo.ExpectedUpdateInterval}'></td></tr>");
            result.Append("<tr><td>Description</td><td><input disabled type='text' class='form-control' style='max-width:300px'" +
                          $" id='description_{encodedId}' value='{sensorInfo.Description}'></td></tr>");
            result.Append("<tr><td>Unit</td><td><input disabled type='text' class='form-control' style='max-width:300px'" +
                          $" id='unit_{encodedId}' value='{sensorInfo.Unit}'></td></tr>");

            result.Append("</div>");
            return result.ToString();
        }

        public static string CreateEditButtonForInfo(string encodedId)
        {
            return $"<button id='editInfo_{encodedId}' style='margin-left: 5px' type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                   " title='edit meta info'><i class='fas fa-edit'></i></button>";
        }

        public static string CreateSaveButtonForInfo(string encodedId)
        {
            return $"<button disabled id='saveInfo_{encodedId}' style='margin-left: 5px' type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                   " title='save meta info'><i class='fas fa-check'></i></button>";
        }

        public static string CreateResetButtonForInfo(string encodedId)
        {
            return $"<button disabled style='margin-left: 5px' id='revertInfo_{encodedId}' type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                " title='revert changes'><i class='fas fa-times'></i></button></td></tr>";
        }
        #endregion
    }
}
