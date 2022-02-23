using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class TreeHelper
    {
        private const int MaxLengthName = 35;

        public static string CreateTree(TreeViewModel model)
        {
            if (model == null) 
                return string.Empty;

            StringBuilder result = new StringBuilder();
            result.Append("<div class='col-md-auto'><div id='jstree'><ul>");
            if (model.Nodes != null)
                foreach (var (_, node) in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            result.Append("</ul></div></div>");

            return result.ToString();
        }

        public static string UpdateTree(TreeViewModel model)
        {
            if (model == null) 
                return string.Empty;

            var result = new StringBuilder();
            if (model.Nodes != null)
                foreach (var (_, node) in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            return result.ToString();
        }

        public static string Recursion(NodeViewModel node)
        {
            var result = new StringBuilder();
            var name = SensorPathHelper.Encode(node.Path);
            var shortName = node.Name.Length > MaxLengthName 
                ? node.Name.Substring(0, MaxLengthName) + "..." : node.Name;

            result.Append($"<li id='{name}' title='{node.Name} &#013;{node.UpdateTime}'" +
                          "data-jstree='{\"icon\" : \"fas fa-circle " +
                          ViewHelper.GetStatusHeaderColorClass(node.Status) + 
                          "\"}'>" + $"{shortName} ({node.Count} sensors)");

            if (node.Nodes != null)
                foreach (var (_, child) in node.Nodes)
                {
                    result.Append($"<ul>{Recursion(child)}</ul>");
                }

            if (node.Sensors != null && !node.Sensors.IsEmpty)
            {
                result.Append("<ul>");
                foreach (var (sensorName, sensor) in node.Sensors)
                {
                    shortName = sensorName.Length > MaxLengthName
                        ? sensorName.Substring(0, MaxLengthName) + "..." : sensorName;

                    var encodedPath = SensorPathHelper.Encode($"{node.Path}/{sensorName}");
                    result.Append($"<li id='sensor_{encodedPath}' title='{sensorName} &#013;{sensor.Time}'" +
                                  "data-jstree='{\"icon\" : \"fas fa-circle " +
                                  ViewHelper.GetStatusHeaderColorClass(sensor.Status) +
                                  "\"}'>" + shortName + "</li>");
                }

                result.Append("</ul>");
            }

            result.Append("</li>");

            return result.ToString();
        }
    }
}