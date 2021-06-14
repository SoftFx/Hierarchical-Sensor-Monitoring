using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.DataLayer.Model;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

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

        public static string CreateTable(List<ProductViewModel> products)
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
                "<th scope='col'>Action</th></tr></thead>");

            result.Append("<tbody>");

            if (products == null || products.Count == 0) return result.ToString();

            //create 
            result.Append("<tr><th>0</th>" +
                "<th><input id='createName' type='text' class='form-control'/>" +
                "<span style='display: none;' id='new_product_name_span'></th>" +
                "<th>---</th>" +
                "<th>---</th>" +
                "<th><button id='createButton' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");

            int index = 1;
            foreach (var product in products)
            {
                string name = product.Name.Replace(' ', '-');
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{product.Name}</td><td id='key_{name}'>{product.Key} " +
                    $"<button id='copy_{name}' data-clipboard-text='{product.Key}' title='copy key' type='button' class='btn btn-secondary'>" +
                    $"<i class='far fa-copy'></i></button></td><td>{product.DateAdded}</td>" +
                    $"<td><button id='delete_{name}' type='button' class='btn btn-secondary' title='delete'>" +
                    $"<i class='fas fa-trash-alt'></i></button></td></tr>");
                index++;
            }

            result.Append("</tbody></table></div></div>");

            return result.ToString();
        }

        public static string CreateTable(List<UserViewModel> users)
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
                "<th scope='col'>Products</th>" + 
                "<th scope='col'>Action</th></tr></thead>");

            result.Append("<tbody>");

            if (users == null || users.Count == 0) return result.ToString();

            //create 
            result.Append("<tr><th>0</th>" +
                "<th><input id='createName' type='text' class='form-control'/></th>" +
                "<th><input id='createPassword' type='password' class='form-control'/></th>" +
                $"<th>{CreateRoleSelect()}</th>" +
                $"<td>See Below</td>" +
                "<th><button id='createButton' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");
            result.Append($"<tr><td colspan='6'>{CreateProductCheckboxs()}</td></tr>");

            int index = 1;
            foreach (var user in users)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{user.Username}</td>" +
                    $"<td>**************</td>" +
                    $"<td>{CreateUserRoleSelect(user.Username, user.Role)}</td>" +
                    $"<td>{CreateUserProductList(user.ProductKeys)}</td>" +
                    $"<td><button id='delete_{user.Username}' type='button' class='btn btn-secondary' title='delete'>" +
                    $"<i class='fas fa-trash-alt'></i></button>" +
                    $"<button style='margin-left: 10px' id='change_{user.Username}' type='button' class='btn btn-secondary' title='change'>" +
                    $"<i class='fas fa-user-edit'></i>" +
                    $"<button disabled style='margin-left: 10px' id='ok_{user.Username}' type='button' class='btn btn-secondary' title='ok'>" +
                    $"<i class='fas fa-check'></i></button>" +
                    $"<button disabled style='margin-left: 10px' id='cancel_{user.Username}' type='button' class='btn btn-secondary' title='cancel'>" +
                    $"<i class='fas fa-times'></i></button></td></tr>");

                result.Append($"<tr><td colspan='6'>" +
                    $"{CreateUserProductCheckboxs(user.Username, user.ProductKeys)}</td></tr>");
                index++;
            }

            result.Append("</tbody></table></div></div>");

            return result.ToString();
        }

        private static string CreateUserProductList(List<string> userProductKeys)
        {
            var response = _client.GetAsync($"{ViewConstants.ApiServer}/api/view/GetProducts").Result;

            List<Product> products = null;
            if (response.IsSuccessStatusCode)
            {
                products = response.Content.ReadAsAsync<List<Product>>().Result;
            }

            StringBuilder result = new StringBuilder();
            if (products == null || products.Count == 0
                || userProductKeys == null || userProductKeys.Count == 0) return "---";

            if (products != null && products.Count > 0)
                foreach (var product in products)
                {   
                    if (userProductKeys != null
                    && userProductKeys.FirstOrDefault(x => x.Equals(product.Key)) != null)
                        result.Append($"{product.Name}; ");
                }

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

        private static string CreateProductCheckboxs()
        {
            var response = _client.GetAsync($"{ViewConstants.ApiServer}/api/view/GetProducts").Result;

            List<Product> products = null;
            if (response.IsSuccessStatusCode)
            {
                products = response.Content.ReadAsAsync<List<Product>>().Result;
            }

            StringBuilder result = new StringBuilder();
            if (products == null || products.Count == 0) return string.Empty;

            //header
            result.Append("<div class='accordion' id='createAccrodion'>" +
                "<div class='accordion-item'>" +
                "<h2 class='accordion-header' id='createHeader'>" +
                "<button class='accordion-button collapsed' type='button' " +
                "data-bs-toggle='collapse' data-bs-target='#createCollapse' aria-expanded='false'" +
                " aria-controls='createCollapse'>New user products:</button></h2>");

            //body
            result.Append("<div id='createCollapse' class='accordion-collapse collapse' " +
                "aria-labelledby='createHeader' data-bs-parent='#createAccordion'>" +
                "<div class='accordion-body'>");

            foreach(var product in products)
            {
                string name = product.Name.Replace(' ', '-');
                result.Append("<div class='form-check'>" +
                    $"<input class='form-check-input' type='checkbox' value='{product.Key}' id='createCheck_{name}'>" +
                    $"<label class='form-check-label' for='createCheck_{name}'>{product.Name}</label></div>");
            }

            result.Append("</div></div></div></div>");

            return result.ToString();
        }

        private static string CreateUserProductCheckboxs(string username, List<string> userProductKeys)
        {
            var response = _client.GetAsync($"{ViewConstants.ApiServer}/api/view/GetProducts").Result;

            List<Product> products = null;
            if (response.IsSuccessStatusCode)
            {
                products = response.Content.ReadAsAsync<List<Product>>().Result;
            }

            StringBuilder result = new StringBuilder();
            if (products == null || products.Count == 0) return string.Empty;

            //header
            result.Append($"<div class='accordion' id='createAccrodion_{username}'>" +
                "<div class='accordion-item'>" +
                $"<h2 class='accordion-header' id='createHeader_{username}'>" +
                $"<button id='accordionButton_{username}' class='accordion-button collapsed' type='button' " +
                $"data-bs-toggle='collapse' data-bs-target='#createCollapse_{username}' aria-expanded='false'" +
                $" aria-controls='createCollapse_{username}'>{username} products:</button></h2>");

            //body
            result.Append($"<div id='createCollapse_{username}' class='accordion-collapse collapse' " +
                $"aria-labelledby='createHeader_{username}' data-bs-parent='#createAccordion_{username}'>" +
                "<div class='accordion-body'>");

            if (userProductKeys != null && userProductKeys.Count > 0)
                foreach(var key in userProductKeys)
                {
                    var product = products.First(x => x.Key.Equals(key));
                    products.Remove(product);
                    string name = product.Name.Replace(' ', '-');

                    result.Append("<div class='form-check'>" +
                        $"<input class='form-check-input' type='checkbox' value='{product.Key}' id='check{username}_{name}' checked disabled>" +
                        $"<label class='form-check-label' for='check{username}_{name}'>{product.Name}</label></div>");
                }

            foreach (var product in products)
            {
                string name = product.Name.Replace(' ', '-');

                result.Append("<div class='form-check'>" +
                    $"<input class='form-check-input' type='checkbox' value='{product.Key}' id='check{username}_{name}' disabled>" +
                    $"<label class='form-check-label' for='check{username}_{name}'>{product.Name}</label></div>");
            }

            result.Append("</div></div></div></div>");

            return result.ToString();
        }
    }
}
