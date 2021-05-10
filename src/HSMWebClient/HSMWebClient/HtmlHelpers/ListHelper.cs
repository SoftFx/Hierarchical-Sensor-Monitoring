using HSMWebClient.Models;
using Microsoft.AspNetCore.Html;
using System.Linq;
using System.Text;

namespace HSMWebClient.HtmlHelpers
{
    public static class ListHelper
    {
        public static HtmlString CreateFullList(TreeViewModel model)
        {
            StringBuilder result = new StringBuilder();

            result.Append("<ul class=\"list-group\">");
            if (model.Nodes != null)
                foreach (var node in model.Nodes)
                {
                    result.Append(GetStringRecursion(node));
                }

            result.Append("</ul>");

            return new HtmlString(result.ToString());
        }

        public static HtmlString CreateList(string path, TreeViewModel model)
        {
            if (path == null) return new HtmlString(string.Empty);

            var nodes = path.Split('/');
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));
            NodeViewModel node = existingNode;
            if (nodes[0].Length < path.Length)
            {
                path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
                node = GetNodeRecursion(path, existingNode);
            }
            
            StringBuilder result = new StringBuilder();

            result.Append("<ul class=\"list-group\">");
            if (node.Sensors != null)
                foreach(var sensor in node.Sensors)
                {
                    result.Append("<li class=\"list-group-item list-group-item-action\">"
                        + $"{sensor.Name} {sensor.Value}" + "</li>");
                }
            result.Append("</ul>");

            return new HtmlString(result.ToString());
        }

        public static string GetStringRecursion(NodeViewModel node)
        {
            StringBuilder result = new StringBuilder();

            if (node.Sensors != null)
                foreach (var sensor in node.Sensors)
                {
                    result.Append("<li class=\"list-group-item list-group-item-action\">"
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
            var nodes = path.Split('/');

            if (nodes[0].Length == path.Length)
                return model.Nodes.First(x => x.Name.Equals(nodes[0]));

            path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));
            return GetNodeRecursion(path, existingNode);
        }
    }
}
