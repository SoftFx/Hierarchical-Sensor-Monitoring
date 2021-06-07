using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class TableHelper
    {
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
                "<th><input id='createName' type='text' class='form-control'/></th>" +
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
                "<th>---</th>" + //todo multiselect - list products
                "<th><button id='createButton' type='button' class='btn btn-secondary' title='create'>" +
                    $"<i class='fas fa-plus'></i></button></th></tr>");

            int index = 1;
            foreach (var user in users)
            {
                result.Append($"<tr><th scope='row'>{index}</th>" +
                    $"<td>{user.Username}</td>" +
                    $"<td>**************</td>" +
                    $"<td>{user.Role}</td>" +
                    $"<td>Products</td>" + //todo products
                    $"<td><button id='delete_{user.Username}' type='button' class='btn btn-secondary' title='delete'>" +
                    $"<i class='fas fa-trash-alt'></i></button></td></tr>");
                index++;
            }

            result.Append("</tbody></table></div></div>");

            return result.ToString();
        }

        private static string CreateRoleSelect()
        {
            StringBuilder result = new StringBuilder();

            result.Append("<select class='form-select'>" +
                $"<option>{UserRoleEnum.DataViewer}</option>" +
                $"<option>{UserRoleEnum.Admin}</option>" +
                $"</select>");

            return result.ToString();
        }
    }
}
