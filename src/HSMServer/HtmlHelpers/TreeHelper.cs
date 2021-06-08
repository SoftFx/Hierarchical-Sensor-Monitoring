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

            result.Append($"<li id='{node.Path.Replace(' ', '-')}'>" + node.Name);
            if (node.Nodes != null)
                foreach (var subnode in node.Nodes)
                {
                    result.Append($"<ul style='color:{GetStatusHeaderColor(subnode.Status)}'>" + Recursion(subnode) + "</ul>");
                }

            result.Append("</li>");

            return result.ToString();
        }

        public static string GetStatusHeaderColor(SensorStatus status)
        {
            switch (status)
            {
                case SensorStatus.Unknown:
                    return "gray";
                case SensorStatus.Ok:
                    return "green";
                case SensorStatus.Warning:
                    return "yellow";
                case SensorStatus.Error:
                    return "red";
                default:
                    return "gray";
            }
        }
    }
}
