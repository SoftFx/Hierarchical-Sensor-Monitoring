using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMServer.Model.ViewModel;

namespace HSMServer.HtmlHelpers
{
    public static class ListHelper
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";

        public static string CreateFullLists(TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();

            result.Append("<div class='col-sm-7' id='list'>" +
                "<div id='list_sensors_header' style='display: none;'>" +
                "<h5 style='margin: 0px 20px 10px;'>Sensors</h5></div>");

            result.Append("<div style='width: 700px'>" +
                "<ul id='noData' style='display: none;' class='list-group'>" +
                "<li class='list-group-item'>No Data</li></ul></div>");

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
            string formattedPath = fullPath.Replace(' ', '-');

            result.Append($"<div class='accordion' id='list_{formattedPath}' style='display: none;'>");
            if (node.Sensors != null)
                foreach (var sensor in node.Sensors)
                {
                    string name = sensor.Name.Replace(' ', '-');
                    result.Append("<div class='accordion-item'>" +
                                  $"<h2 class='accordion-header' id='heading_{formattedPath}_{name}'>");

                    if (sensor.SensorType == SensorType.FileSensor || sensor.SensorType == SensorType.FileSensorBytes)
                    {
                        //header
                        string fileName = GetFileNameString(sensor.Value);

                        //button
                        result.Append($"<button id='{formattedPath}_{name}' class='accordion-button' style='display: none' type='button' data-bs-toggle='collapse'" +
                                  $"data-bs-target='#collapse_{formattedPath}_{name}' aria-expanded='true' aria-controls='collapse_{formattedPath}_{name}'>" +
                                  "<div class='container'>" +
                                  $"<div class='row row-cols-1'><div class='col'>{sensor.Name}</div>" +
                                  $"<div class='col'>{sensor.Value}</div></div></div></button></h2>");
                        //body
                        result.Append($"<div id='collapse_{formattedPath}_{name}' class='accordion-collapse' " +
                                  $"aria-labelledby='heading_{formattedPath}_{name}' data-bs-parent='#list_{formattedPath}'>" +
                                  "<div class='accordion-body'>");

                        result.Append("<div class='container'>" +
                                  $"<div class='row row-cols-1'><div class='col'><li class='fas fa-circle sensor-icon-with-margin " +
                                  $"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>{sensor.Name}</div>" +
                                  $"<div class='col'>{sensor.Value}</div></div></div>" +
                                      "<div class='row'><div class='col-2'>" +
                                      $"<button id='button_view_{formattedPath}_{name}_{fileName}' " +
                                      "class='button-view-file-sensor btn btn-secondary' title='View'>" +
                                      "<i class='fas fa-eye'></i></button></div>" +
                                      "<div class='col'>" +
                                      $"<button id='button_download_{formattedPath}_{name}_{fileName}'" +
                                      " class='button-download-file-sensor-value btn btn-secondary'" +
                                      " title='Download'><i class='fas fa-file-download'></i></button></div></div>");


                        result.Append("</div></div></div>");

                        continue;
                    }

                    result.Append($"<button id='{formattedPath}_{name}' class='accordion-button collapsed' type='button' data-bs-toggle='collapse'" +
                                  $"data-bs-target='#collapse_{formattedPath}_{name}' aria-expanded='false' aria-controls='collapse_{formattedPath}_{name}'>" +
                                  "<div class='container'>" +
                                  "<div class='row row-cols-1'><div class='col'><li class='fas fa-circle sensor-icon-with-margin " +
                                  $"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>{sensor.Name}</div>" +
                                  $"<div class='col'>{sensor.Value}</div></div></div></button></h2>");

                    result.Append($"<div id='collapse_{formattedPath}_{name}' class='accordion-collapse collapse'" +
                                  $"aria-labelledby='heading_{formattedPath}_{name}' data-bs-parent='#list_{formattedPath}'>" +
                                  "<div class='accordion-body'>" +
                                  "<div class='mb-3 row'>" +
                                  $"<label for='inputCount_{formattedPath}_{name}' class='col-sm-2 col-form-label'>Total Count</label>" +
                                  "<div class='col-sm-2'>" +
                                  $"<input type='number' class='form-control' id='inputCount_{formattedPath}_{name}' value='10' min='10'></div>" +
                                  "<div class='col-sm-1'>" +
                                  $"<button id='reload_{formattedPath}_{name}' type='button' class='btn btn-secondary'>" +
                                  "<i class='fas fa-redo-alt'></i></button></div>" +
                                  $"<div class='col-sm-1'><button id='button_graph_{formattedPath}_{name}_{(int)sensor.SensorType}' type='button' class='btn btn-secondary'>" +
                                  "<i class='fas fa-chart-bar'></i></div>" +
                                  $"<div id='values_{formattedPath}_{name}'></div>" +
                                  $"<div id='graph_{formattedPath}_{name}' style='display: none'></div></div></div></div></div>");



                }
            result.Append("</div>");

            return result.ToString();
        }

        private static string GetFileNameString(string shortValue)
        {
            var ind = shortValue.IndexOf(FileNamePattern);
            if (ind != -1)
            {
                var fileNameString = shortValue.Substring(ind + FileNamePattern.Length);
                int firstDotIndex = fileNameString.IndexOf('.');
                int secondDotIndex = fileNameString.Substring(firstDotIndex + 1).IndexOf('.');
                return fileNameString.Substring(0, firstDotIndex + secondDotIndex + 1);
            }

            ind = shortValue.IndexOf(ExtensionPattern);
            if (ind != -1)
            {
                var extensionString = shortValue.Substring(ind + ExtensionPattern.Length);
                int dotIndex = extensionString.IndexOf('.');
                return extensionString.Substring(0, dotIndex);
            }

            return string.Empty;
        }

        public static string CreateHistoryList(List<SensorHistoryData> sensors)
        {
            if (sensors == null) return string.Empty;

            StringBuilder result = new StringBuilder();
            result.Append("<div class='col-xxl' style='margin: 10px 0px'><ul class='list-group'>");

            foreach(var sensor in sensors)
            {
                result.Append("<li class='list-group-item list-group-item-action'>"
                        + $"{sensor.Time}: {sensor.TypedData}" + "</li>");
            }

            result.Append("</ul></div>");
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
