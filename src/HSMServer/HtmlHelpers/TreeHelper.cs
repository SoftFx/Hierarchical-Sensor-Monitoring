using Microsoft.AspNetCore.Html;
using System.Text;
using HSMServer.Model.ViewModel;

namespace HSMServer.HtmlHelpers
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

            result.Append($"<li id=\"{node.Path.Replace(' ', '_')}\">" + node.Name);
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
