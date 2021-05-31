using System;
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


        public static string CreateFullLists(TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();
            result.Append("<div class='col-sm-8' id='list'>" +
                "<div id='list_sensors_header' style='display: none;'>" +
                "<p style='margin: 0px 20px 10px;'>Sensors</p></div>");

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

                    if (sensor.SensorType != SensorType.FileSensor)
                    {
                        result.Append("<div class='accordion-item'>" +
                                      $"<h2 class='accordion-header' id='heading_{formattedPath}_{name}'>" +
                                      $"<button id='{formattedPath}_{name}' class='accordion-button collapsed' type='button' data-bs-toggle='collapse'" +
                                      $"data-bs-target='#collapse_{formattedPath}_{name}' aria-expanded='false' aria-controls='collapse_{formattedPath}_{name}'>" +
                                      $"{sensor.Name} {sensor.Value}</button></h2>");

                        result.Append($"<div id='collapse_{formattedPath}_{name}' class='accordion-collapse collapse'" +
                                      $"aria-labelledby='heading_{formattedPath}_{name}' data-bs-parent='#list_{formattedPath}'>" +
                                      $"<div class='accordion-body'>" +
                                      $"<div class='mb-3 row'>" +
                                      $"<label for='inputCount_{formattedPath}_{name}' class='col-sm-2 col-form-label'>Total Count</label>" +
                                      $"<div class='col-sm-2'>" +
                                      $"<input type='number' class='form-control' id='inputCount_{formattedPath}_{name}' value='10' min='10'></div>" +
                                      $"<div class='col-sm-2'>" +
                                      $"<button id='reload_{formattedPath}_{name}' type='button' class='btn btn-secondary'><i class='fas fa-redo-alt'></i></button></div>" +
                                      $"<div id='values_{formattedPath}_{name}'></div></div></div></div></div>");
                    }
                    else
                    {
                        result.Append("<div class='accordion-item'><div class='file-sensor-shortvalue-div'" +
                                      $"<h2 class='accordion-header' id='heading_{formattedPath}_{name}'>" +
                                      $"<div class='col-md-auto'>{sensor.Name} {sensor.Value}</div>" +
                                      $"<div class='col-md-auto'>" +
                                      $"<button id='button_view_{formattedPath}_{name}' class='button-view-file-sensor btn btn-secondary' title='View'>" +
                                        "<i class='fas fa-eye'></i></button></div" +
                                      $"<div class='col-md-auto'><button id='button_download_{formattedPath}_{name}' class='button-download-file-sensor-value btn btn-secondary'" +
                                        " title='Download'><i class='fas fa-file-download'></i></button></div>" +
                                      "</h2></div></div>");
                        //result.Append("<div class='accordion-item'>" +
                        //              $"</div>");
                    }

                }
            result.Append("</div>");

            return result.ToString();
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
