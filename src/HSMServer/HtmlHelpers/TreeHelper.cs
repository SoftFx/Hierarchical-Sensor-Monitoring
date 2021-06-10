using System.Text;
using HSMSensorDataObjects;
using HSMServer.Model.ViewModel;

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

        public static string Recursion(NodeViewModel node)
        {
            StringBuilder result = new StringBuilder();

            result.Append($"<li id='{node.Path.Replace(' ', '-')}' " +
                          "data-jstree='{\"icon\":\"fas fa-circle " +
                          GetStatusHeaderColorClass(node.Status) +
                          "\"}'>" + node.Name);

            if (node.Nodes != null)
                foreach (var subnode in node.Nodes)
                {
                    result.Append("<ul>" + Recursion(subnode) + "</ul>");
                }

            result.Append("</li>");

            return result.ToString();
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
//style = 'color:{GetStatusHeaderColor(subnode.Status)}'