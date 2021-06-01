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
                "<h5 style='margin: 10px 20px 10px;'>Products</h5></div>" +
                "<div class='col'>" +
                "<button id='add_{product.Key}' type='button' class='btn btn-secondary'>" +
                    $"Add new product <i class='fas fa-plus'></i></button>" +
                    $"</div></div></div>");

            result.Append("<div class='col-xxl'>");
            //table template
            result.Append("<table class='table table-striped'><thead><tr>" +
                "<th scope='col'>#</th>" +
                "<th scope='col'>Name</th>" +
                "<th scope='col'>Key</th>" +
                "<th scope='col'>Creation Date</th>" +
                "<th scope='col'>Delete</th></tr></thead>");

            result.Append("<tbody>");

            if (products == null || products.Count == 0) return result.ToString();

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
    }
}
