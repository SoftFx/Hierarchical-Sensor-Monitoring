using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Html;
using System.Collections.Generic;
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

        public static HtmlString CreateProductList(List<ProductViewModel> products)
        {
            return new HtmlString(TableHelper.CreateTable(products));
        }

        public static HtmlString CreateUserList(List<UserViewModel> users)
        {
            return new HtmlString(TableHelper.CreateTable(users));
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
