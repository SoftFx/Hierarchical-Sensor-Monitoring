using HSMWebClient.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text;

namespace HSMWebClient
{
    public static class TreeHelper
    {
        public static HtmlString CreateTree(this IHtmlHelper helper, TreeViewModel model)
        {
            StringBuilder result = new StringBuilder();

            result.Append("<ul>");
            if (model.Nodes != null)
                foreach (var node in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            result.Append("</ul>");

            return new HtmlString(result.ToString());
        }

        public static string Recursion(NodeViewModel node)
        {
            StringBuilder result = new StringBuilder();

            result.Append("<li>" + node.Name);
            if (node.Nodes != null)
                foreach (var subnode in node.Nodes)
                {
                    result.Append("<ul>" + Recursion(subnode) + "</ul>");
                }

            //if (node.Sensors != null)
            //    foreach (var sensor in node.Sensors)
            //    {
            //        //result.Append("<li class=\"list-group-item list-group-item-action\">" + sensor.Name + "</li>");
            //        result.Append("<li>" + sensor.Name + "</li>");
            //    }
            result.Append("</li>");

            return result.ToString();
        }
    }
}
