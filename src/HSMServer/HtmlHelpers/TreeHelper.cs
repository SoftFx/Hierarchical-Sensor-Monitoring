using HSMServer.Constants;
using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class TreeHelper
    {
        private const int NodeNameMaxLength = 35;

        public static string CreateTree(TreeViewModel model)
        {
            if (model == null)
                return string.Empty;

            var result = new StringBuilder(model.Nodes?.Count ?? 0 + 2);
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

            var result = new StringBuilder(model.Nodes?.Count ?? 0);
            if (model.Nodes != null)
                foreach (var (_, node) in model.Nodes)
                {
                    result.Append(Recursion(node));
                }

            return result.ToString();
        }

        public static string Recursion(NodeViewModel node)
        {
            var result = new StringBuilder(node.Nodes?.Count ?? 0 + node.Sensors?.Count ?? 0 + 8);
            var name = SensorPathHelper.Encode(node.Path);
            var shortName = node.Name.Length > NodeNameMaxLength
                ? $"{node.Name[..NodeNameMaxLength]}..."
                : node.Name;

            result.Append($"<li id='{name}' title='{node.Name} &#013;{node.UpdateTime}'")
                  .Append("data-jstree='{\"icon\" : \"fas fa-circle ")
                  .Append(ViewHelper.GetStatusHeaderColorClass(node.Status))
                  .Append($"\", \"time\" : \"{node.UpdateTime.ToString(ViewConstants.NodeUpdateTimeFormat)}\"")
                  .Append($"}}'>{shortName} ({node.Count} sensors)");

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
                    shortName = sensorName.Length > NodeNameMaxLength
                        ? $"{sensorName[..NodeNameMaxLength]}..."
                        : sensorName;

                    var encodedPath = SensorPathHelper.Encode($"{node.Path}/{sensorName}");
                    result.Append($"<li id='sensor_{encodedPath}' title='{sensorName} &#013;{sensor.Time}'")
                          .Append("data-jstree='{\"icon\" : \"fas fa-circle ")
                          .Append(ViewHelper.GetStatusHeaderColorClass(sensor.Status))
                          .Append($"\", \"time\" : \"{sensor.Time}\"}}'>{shortName}</li>");
                }

                result.Append("</ul>");
            }

            result.Append("</li>");

            return result.ToString();
        }
    }
}