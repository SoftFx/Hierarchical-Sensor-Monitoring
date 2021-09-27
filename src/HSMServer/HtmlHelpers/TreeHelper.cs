using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class TreeHelper
    {
        public static string CreateTree(TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();
            result.Append("<div class='col-md-auto'><div id='jstree'><ul>");
            if (model.Nodes != null)
                foreach (var node in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            result.Append("</ul></div></div>");

            return result.ToString();
        }

        public static string UpdateTree(TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();
            if (model.Nodes != null)
                foreach (var node in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            return result.ToString();
        }

        public static string Recursion(NodeViewModel node)
        {
            StringBuilder result = new StringBuilder();
            var name = SensorPathHelper.Encode(node.Path);

            result.Append($"<li id='{name}' " +
                          "data-jstree='{\"icon\" : \"fas fa-circle " +
                          ViewHelper.GetStatusHeaderColorClass(node.Status) + 
                          "\"}'>" + $"{node.Name} ({node.Count} sensors)");

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