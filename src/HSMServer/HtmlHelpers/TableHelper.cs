using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.ApiControllers;
using HSMServer.Constants;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HSMServer.HtmlHelpers
{
    public static class TableHelper
    {
        private static readonly HttpClientHandler _clientHandler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback =
            (sender, cert, chain, sslPolicyErrors) => { return true; }
        };
        private static readonly HttpClient _client = new HttpClient(_clientHandler);

        #region [ Users ]
        public static string CreateTable(User user, List<UserViewModel> users)
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

                result.Append($"<td>{CreateUserProductsList(userItem.ProductsRoles)}</td>");

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

        private static string CreateUserProductsList(List<KeyValuePair<string, ProductRoleEnum>> productsRights)
        {
            StringBuilder result = new StringBuilder();

            if (productsRights == null || !productsRights.Any())
                return "---";

            var response = _client.GetAsync(
                $"{ViewConstants.ApiServer}/api/view/{nameof(ViewController.GetAllProducts)}").Result;

            List<Product> products = null;
            if (response.IsSuccessStatusCode)
            {
                products = response.Content.ReadAsAsync<List<Product>>().Result;
            }

            foreach (var right in productsRights)
            {
                var name = products?.FirstOrDefault(p => p.Key.Equals(right.Key))?.Name;
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
                result.Append("<th scope='col'>Action</th></tr>");

            result.Append("</thead><tbody>");
           
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

            if (products == null || products.Count == 0) return result.ToString();

            int index = 1;
            foreach (var product in products)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{product.Name}</td>" +
                    $"<td id='key_{product.Key}' value='{product.Key}'>{product.Key}</td> " +
                    $"<td><button id='copy_{product.Key}' data-clipboard-text='{product.Key}' title='copy key' type='button' class='btn btn-secondary'>" +
                    "<i class='far fa-copy'></i></button>" +
                    $"<input style='display: none' type='text' id='inputName_{product.Key}' value='{product.Name}'/></td>" +
                    $"<td>{product.CreationDate}</td>" +
                    $"<td>{product.ManagerName}</td>");


                if (UserRoleHelper.IsProductCRUDAllowed(user) || 
                    ProductRoleHelper.IsManager(product.Key, user.ProductsRoles))
                    result.Append($"<td><button style='margin-left: 5px' id='change_{product.Key}' " +
                    "type='button' class='btn btn-secondary' title='edit'>" +
                    "<i class='fas fa-edit'></i></button>");

                if (UserRoleHelper.IsProductCRUDAllowed(user))
                    result.Append($"<button id='delete_{product.Key}' style='margin-left: 5px' " +
                        "type='button' class='btn btn-secondary' title='delete'>" +
                        "<i class='fas fa-trash-alt'></i></button>");

                result.Append("</tr>");
                index++;
            }

            result.Append("</tbody></table></div></div>");

            return result.ToString();
        }

        #endregion

        #region [ Edit Product: User Right ]

        public static string CreateTable(string productName, User user,
            List<KeyValuePair<UserViewModel, ProductRoleEnum>> usersRights)
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
                    $"<th>{CreateUserSelect(usedUsers)}" +
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

        private static string CreateUserSelect(List<UserViewModel> usedUsers)
        {
            var response = _client.GetAsync(
                $"{ViewConstants.ApiServer}/api/view/{nameof(ViewController.GetUsersNotAdmin)}").Result;

            List<User> users = null;
            if (response.IsSuccessStatusCode)
            {
                users = response.Content.ReadAsAsync<List<User>>().Result;
            }

            RemovedUsedUsers(users, usedUsers);

            StringBuilder result = new StringBuilder();
            
            if (users != null && users.Any())
            {
                result.Append("<select class='form-select' id='createUser'>");

                foreach (var user in users)
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

        #region [ Edit Product: Extra Key ]

        public static string CreateTable(string productName, User user, List<ExtraKeyViewModel> extraKeys)
        {
            StringBuilder result = new StringBuilder();
            //header template
            result.Append("<div style='margin: 10px'>" +
                "<div class='row justify-content-start'>" +
                $"<h5 style='margin: 10px 20px 10px;'>Edit Product '{productName}' Extra Keys</h5></div></div>");


            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'><thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Extra Key Name</th>" +
                "<th scope='col'>Key</th>" +
                "<th scope='col'>Action</th></tr>");

            result.Append("</thead><tbody>");

            //create 
            result.Append("<tr><th>0</th>" +
                    $"<th><input id='createKeyName' type='text' class='form-control'/>" +
                    $"<span style='display: none;' id='new_key_span'></th>" +
                    $"<th>---</th>" +
                    "<th><button id='createKeyButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");

            if (extraKeys == null || !extraKeys.Any())
            {
                result.Append("</tbody></table></div>");
                return result.ToString();
            }

            int index = 1;
            foreach (var extraKey in extraKeys)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{extraKey.ExtraKeyName}" +
                    $"<input id='keyName_{extraKey.ExtraProductKey}' value='{extraKey.ExtraKeyName}' style='display: none'/></td>" +
                    $"<td>{extraKey.ExtraProductKey} " +
                    $"<button id='copy_{extraKey.ExtraProductKey}' data-clipboard-text='{extraKey.ExtraProductKey}' title='copy key' type='button' class='btn btn-secondary'>" +
                    $"<i class='far fa-copy'></i></button>" +
                    $"</td>");

                result.Append($"<td><button id='deleteKey_{extraKey.ExtraProductKey}' style='margin-left: 5px' " +
                    $"type='button' class='btn btn-secondary' title='delete'>" +
                    $"<i class='fas fa-trash-alt'></i></button></td>");

                result.Append("</tr>");
                index++;
            }

            result.Append("</tbody></table></div>");
            return result.ToString();
        }

        #endregion

        #region Sensor history tables

        public static string CreateHistoryTable(List<SensorHistoryData> sensorHistory)
        {
            if (sensorHistory.Count == 0)
                return string.Empty;

            sensorHistory.Reverse();

            var type = sensorHistory[0].SensorType;
            switch (type)
            {
                case SensorType.BooleanSensor:
                    return CreateBooleanTable(sensorHistory.Select(h =>
                    JsonSerializer.Deserialize<BoolSensorData>(h.TypedData)).ToList(), 
                    sensorHistory.Select(h => h.Time).ToList());
                case SensorType.IntSensor:
                    return CreateIntegerTable(sensorHistory.Select(h =>
                        JsonSerializer.Deserialize<IntSensorData>(h.TypedData)).ToList(),
                        sensorHistory.Select(h => h.Time).ToList());
                case SensorType.DoubleSensor:
                    return CreateDoubleTable(sensorHistory.Select(h =>
                        JsonSerializer.Deserialize<DoubleSensorData>(h.TypedData)).ToList(),
                        sensorHistory.Select(h => h.Time).ToList());
                case SensorType.StringSensor:
                    return CreateStringTable(sensorHistory.Select(h =>
                        JsonSerializer.Deserialize<StringSensorData>(h.TypedData)).ToList(),
                        sensorHistory.Select(h => h.Time).ToList());
                case SensorType.IntegerBarSensor:
                    return CreateIntBarTable(sensorHistory.Select(h =>
                        JsonSerializer.Deserialize<IntBarSensorData>(h.TypedData)).ToList(),
                        sensorHistory.Select(h => h.Time).ToList());
                case SensorType.DoubleBarSensor:
                    return CreateDoubleBarData(sensorHistory.Select(h =>
                        JsonSerializer.Deserialize<DoubleBarSensorData>(h.TypedData)).ToList(),
                        sensorHistory.Select(h => h.Time).ToList());
                default:
                    return string.Empty;
            }
        }

        private static string CreateBooleanTable(List<BoolSensorData> boolHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th scope='col'>Date</th>" +
                      "<th scope='col'>Value</th>" +
                      "<th scope='col'>Comment</th></tr></thead><tbody>");

            for(int i = 0; i < boolHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                    $"<td>{boolHistory[i].BoolValue}</td>" +
                    $"<td>{boolHistory[i].Comment}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        private static string CreateIntegerTable(List<IntSensorData> intHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th>Date</th>" +
                      "<th scope='col'>Number</th>" +
                      "<th scope='col'>Comment</th></tr></thead><tbody>");

            for(int i=0; i < intHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                    $"<td scope='row'>{intHistory[i].IntValue}</td>" +
                    $"<td>{intHistory[i].Comment}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        private static string CreateDoubleTable(List<DoubleSensorData> doubleHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th>Date</th>" +
                      "<th scope='col'>Number</th>" +
                      "<th scope='col'>Comment</th></tr></thead><tbody>");

            for (int i=0; i < doubleHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                    $"<td scope='row'>{doubleHistory[i].DoubleValue}</td>" +
                    $"<td>{doubleHistory[i].Comment}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        private static string CreateStringTable(List<StringSensorData> stringHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th>Date</th>" +
                      "<th scope='col'>String value</th>" +
                      "<th scope='col'>Comment</th></tr></thead><tbody>");

            for (int i = 0; i< stringHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                    $"<td scope='row'>{stringHistory[i].StringValue}</td>" +
                    $"<td>{stringHistory[i].Comment}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        private static string CreateIntBarTable(List<IntBarSensorData> intBarHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th>Date</th>" +
                      "<th scope='col'>Min</th>" +
                      "<th scope='col'>Mean</th>" +
                      "<th scope='col'>Median</th>" +
                      "<th scope='col'>Max</th></tr></thead><tbody>");

            for (int i=0; i < intBarHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                    $"<td scope='row'>{intBarHistory[i].Min}</td>" +
                    $"<td>{intBarHistory[i].Mean}</td>" +
                    $"<td>{intBarHistory[i].Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.5) < double.Epsilon)?.Value}" +
                    $"</td><td>{intBarHistory[i].Max}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        private static string CreateDoubleBarData(List<DoubleBarSensorData> doubleBarHistory,
            List<DateTime> dates)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<table class='table table-striped'><thead><tr>" +
                      "<th>Date</th>" +
                      "<th scope='col'>Min</th>" +
                      "<th scope='col'>Mean</th>" +
                      "<th scope='col'>Median</th>" +
                      "<th scope='col'>Max</th></tr></thead><tbody>");

            for (int i = 0; i < doubleBarHistory.Count; i++)
            {
                sb.Append($"<tr><td>{dates[i]}</td>" +
                          $"<td scope='row'>{doubleBarHistory[i].Min}</td>" +
                          $"<td>{doubleBarHistory[i].Mean}</td><td>" +
                          $"{doubleBarHistory[i].Percentiles.FirstOrDefault(p => Math.Abs(p.Percentile - 0.5) < double.Epsilon)?.Value}" +
                          $"</td><td>{doubleBarHistory[i].Max}</td></tr>");
            }

            sb.Append("</tbody>");
            return sb.ToString();
        }

        #endregion

        #region Configuration object

        public static string CreateConfigurationObjectsTable(List<ConfigurationObjectViewModel> configurationObjects)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("<div style='margin: 10px'>" +
                          "<div class='row justify-content-start'><div class='col-auto'>" +
                          "<h5 style='margin: 10px 20px 10px;'>Configuration parameters</h5></div></div></div>");

            sb.Append("<div class='col-xxl'>");
            //table template
            sb.Append("<table class='table table-striped'><thead><tr>" +
                          "<th scope='col'>#</th>" +
                          "<th scope='col'>Parameter name</th>" +
                          "<th scope='col'>Parameter value</th>" +
                          "<th scope='col'>Action</th></tr></thead>");

            sb.Append("<tbody>");

            for (int i = 0; i < configurationObjects.Count; ++i)
            {
                sb.Append($"<tr><th scope='row'>{i}</th><td><label class='config-name'>{configurationObjects[i].Name}</label>" +
                          "<a tabindex='0' data-bs-toggle='popover' data-bs-trigger='focus' title='Description' " +
                          $" data-bs-content='{configurationObjects[i].Description}'><i class='fas fa-question-circle'></i></a></td>" +
                          "<td><div style='display: flex'><input type='text' class='form-control' style='max-width:300px' " +
                          $"value='{configurationObjects[i].Value}' id='value_{configurationObjects[i].Name}'>");

                if (configurationObjects[i].IsDefault)
                {
                    sb.Append("<label class='default-text-field'>default</label>");
                }

                sb.Append("</div></td><td>");

                if (!configurationObjects[i].IsDefault)
                {
                    sb.Append($"<button id='reset_{configurationObjects[i].Name}' style='margin-left: 5px' " +
                              "type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                              " title='reset value to default'><i class='fas fa-undo-alt'></i></button>");
                }

                sb.Append($"<button disabled style='margin-left: 5px' id='ok_{configurationObjects[i].Name}' " +
                          "type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                          " title='ok'><i class='fas fa-check'></i></button>" +

                          $"<button disabled style='margin-left: 5px' id='cancel_{configurationObjects[i].Name}' " +
                          "type='button' class='btn btn-secondary' data-bs-toggle='tooltip'" +
                          " title='revert changes'><i class='fas fa-times'></i></button></td></tr>");
            }

            sb.Append("</tbody></table></div>");
            return sb.ToString();
        }

        #endregion
    }
}
