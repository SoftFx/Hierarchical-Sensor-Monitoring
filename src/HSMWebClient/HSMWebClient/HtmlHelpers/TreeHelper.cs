using HSMWebClient.Models;
using Microsoft.AspNetCore.Html;
using System.Text;

namespace HSMWebClient.HtmlHelpers
{
    public static class TreeHelper
    {
        public static HtmlString CreateTree(TreeViewModel model)
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

            result.Append($"<li id=\"{node.Path}\">" + node.Name);
            if (node.Nodes != null)
                foreach (var subnode in node.Nodes)
                {
                    result.Append("<ul>" + Recursion(subnode) + "</ul>");
                }

            result.Append("</li>");

            return result.ToString();
        }
    }
}
