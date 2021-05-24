using System.Linq;
using System.Text;
using HSMServer.Model.ViewModel;

namespace HSMServer.HtmlHelpers
{
    public static class ListHelper
    {
        public static string CreateFullLists(TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();
            result.Append("<div class='col-sm-8' id='list'>");

            foreach (var path in model.Paths)
            {
                result.Append(CreateList(path, path, model));
            }
            result.Append("</div>");

            return result.ToString();
        }

        public static string CreateList(string path, string fullPath, TreeViewModel model)
        {
            if (path == null) return string.Empty;

            var nodes = path.Split('_');
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));
            NodeViewModel node = existingNode;
            if (nodes[0].Length < path.Length)
            {
                path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
                node = GetNodeRecursion(path, existingNode);
            }
            
            StringBuilder result = new StringBuilder();

            result.Append($"<ul id='list_{fullPath.Replace(' ', '_')}' class='list-group' style='display: none;'>");
            if (node.Sensors != null)
                foreach(var sensor in node.Sensors)
                {
                    result.Append("<li class='list-group-item list-group-item-action'>"
                        + $"{sensor.Name} {sensor.Value}" + "</li>");
                }
            result.Append("</ul>");

            return result.ToString();
        }

        public static string GetStringRecursion(NodeViewModel node)
        {
            StringBuilder result = new StringBuilder();

            if (node.Sensors != null)
                foreach (var sensor in node.Sensors)
                {
                    result.Append("<li class='list-group-item list-group-item-action'>"
                        + $"{sensor.Name} {sensor.Value}" + "</li>");
                }

            if (node.Nodes != null)
                foreach (var subnode in node.Nodes)
                {
                   result.Append(GetStringRecursion(subnode));
                }

            return result.ToString();
        }

        private static NodeViewModel GetNodeRecursion(string path, NodeViewModel model)
        {
            var nodes = path.Split('_');

            if (nodes[0].Length == path.Length)
                return model.Nodes.First(x => x.Name.Equals(nodes[0]));

            path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));

            return GetNodeRecursion(path, existingNode);
        }
    }
}
