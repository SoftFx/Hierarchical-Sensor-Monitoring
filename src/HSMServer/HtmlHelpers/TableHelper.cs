using HSMServer.ApiControllers;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;

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
                "<th scope='col'>Role</th>" + 
                "<th scope='col'>Action</th>");

            result.Append("<tbody>");

            if (users == null || users.Count == 0) return result.ToString();

            //create 
            if (UserRoleHelper.IsUserCRUDAllowed(user.Role))
            {
                result.Append("<tr><th>0</th>" +
                "<th><input id='createName' type='text' class='form-control'/></th>" +
                "<th><input id='createPassword' type='password' class='form-control'/></th>" +
                $"<th>{CreateRoleSelect()}</th>" +
                "<th><button id='createButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                $"<i class='fas fa-plus'></i></button></th></tr>");
            }

            int index = 1;
            foreach (var userItem in users)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{userItem.Username}</td>" +
                    $"<td>**************</td>");

                if (UserRoleHelper.IsUserCRUDAllowed(user.Role))
                    result.Append($"<td>{CreateUserRoleSelect(userItem.Username, userItem.Role.Value)}</td>");
                else
                    result.Append($"<td>{userItem.Role}</td>");

                result.Append("<td style='width: 25%'>");
                if (UserRoleHelper.IsUserCRUDAllowed(user.Role))
                    result.Append($"<button style='margin-left: 5px' id='delete_{userItem.Username}' " +
                        $"type='button' class='btn btn-secondary' title='delete'>" +
                        $"<i class='fas fa-trash-alt'></i></button>");

                result.Append($"<button style='margin-left: 5px' id='change_{userItem.Username}' " +
                    $"type='button' class='btn btn-secondary' title='change'>" +
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

        private static string CreateRoleSelect()
        {
            StringBuilder result = new StringBuilder();

            result.Append("<select class='form-select' id='createRole'>");

            foreach (UserRoleEnum role in Enum.GetValues(typeof(UserRoleEnum)))
                result.Append($"<option value='{(int)role}'>{role}</option>");

            result.Append("</select>");

            return result.ToString();
        }

        private static string CreateUserRoleSelect(string username, UserRoleEnum userRole)
        {
            StringBuilder result = new StringBuilder();
            result.Append($"<select class='form-select' disabled id='role_{username}'>");

            foreach (UserRoleEnum role in Enum.GetValues(typeof(UserRoleEnum)))
            {
                if (role == userRole)
                    result.Append($"<option selected value='{(int)role}'>{role}</option>");
                else
                    result.Append($"<option value='{(int)role}'>{role}</option>");
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
                "<th scope='col'>Creation Date</th>" +
                "<th scope='col'>Manager</th>");

            if (UserRoleHelper.IsProductCRUDAllowed(user.Role) 
                || ProductRoleHelper.IsProductActionAllowed(user.ProductsRoles))
                result.Append("<th scope='col'>Action</th></tr>");

            result.Append("</thead><tbody>");
           
            //create 
            if (UserRoleHelper.IsProductCRUDAllowed(user.Role))
                result.Append("<tr><th>0</th>" +
                    "<th><input id='createName' type='text' class='form-control'/>" +
                    "<span style='display: none;' id='new_product_name_span'></th>" +
                    "<th>---</th>" +
                    $"<th>---</th>" +
                    $"<th>---</th>" +
                    "<th><button id='createButton' style='margin-left: 5px' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");

            if (products == null || products.Count == 0) return result.ToString();

            int index = 1;
            foreach (var product in products)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{product.Name}</td>" +
                    $"<td id='key_{product.Key}' value='{product.Key}'>{product.Key} " +
                    $"<button id='copy_{product.Key}' data-clipboard-text='{product.Key}' title='copy key' type='button' class='btn btn-secondary'>" +
                    $"<i class='far fa-copy'></i></button>" +
                    $"<input style='display: none' type='text' id='inputName_{product.Key}' value='{product.Name}'/></td>" +
                    $"<td>{product.CreationDate}</td>" +
                    $"<td>{product.ManagerName}</td>");


                if (UserRoleHelper.IsProductCRUDAllowed(user.Role) || 
                    ProductRoleHelper.IsManager(product.Key, user.ProductsRoles))
                    result.Append($"<td><button style='margin-left: 5px' id='change_{product.Key}' " +
                    $"type='button' class='btn btn-secondary' title='edit'>" +
                    "<i class='fas fa-edit'></i></button>");

                if (UserRoleHelper.IsProductCRUDAllowed(user.Role))
                    result.Append($"<button id='delete_{product.Key}' style='margin-left: 5px' " +
                        $"type='button' class='btn btn-secondary' title='delete'>" +
                        $"<i class='fas fa-trash-alt'></i></button>");

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
                $"<h5 style='margin: 10px 20px 10px;'>Edit Product '{productName}' Users Rights</h5></div></div>");

            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'>" +
                "<thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Username</th>" +
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

            StringBuilder result = new StringBuilder();
            if (usedUsers != null && usedUsers.Any())
                foreach (var usedUser in usedUsers)
                {
                    var user = users.First(u => u.UserName.Equals(usedUser.Username));
                    users.Remove(user);
                }

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
    }
}
