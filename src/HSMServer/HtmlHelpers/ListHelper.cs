using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.HtmlHelpers
{
    public static class ListHelper
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";

        public static string CreateFullLists(TreeViewModel model)
        {
            if (model == null)
                return string.Empty;

            var result = new StringBuilder(1 << 20);

            result.Append("<div class='col' id='list'>" +
                "<div id='list_sensors_header' style='display: none;'>" +
                "<h5 style='margin: 0px 20px 10px;'>Sensors</h5></div>");

            result.Append("<div style='width: 800px'>" +
                "<ul id='noData' class='list-group'>" +
                "<li class='list-group-item'>No Data</li></ul></div>");

            foreach (var (_, node) in model.Nodes)
            {
                DFSCreateList(result, node);
            }

            result.Append("</div>");

            return result.ToString();
        }

        public static string CreateNotSelectedLists(string selectedPath, TreeViewModel model)
        {
            if (model == null)
                return string.Empty;

            var result = new StringBuilder(1 << 8);

            foreach (var (_, node) in model.Nodes)
            {
                string formattedPath = SensorPathHelper.Encode(node.Path);
                if (!string.IsNullOrEmpty(selectedPath)
                    && selectedPath.Equals(formattedPath))
                    continue;

                DFSCreateList(result, node);
            }

            return result.ToString();
        }

        public static void DFSCreateList(StringBuilder result, NodeViewModel node)
        {
            if (node.Sensors != null && !node.Sensors.IsEmpty)
                CreateList(result, node);

            if (node.Nodes == null || node.Nodes.IsEmpty)
                return;

            foreach (var (_, child) in node.Nodes)
            {
                DFSCreateList(result, child);
            }
        }

        public static StringBuilder CreateList(StringBuilder result, NodeViewModel node)
        {
            string formattedNodePath = SensorPathHelper.Encode(node.Path);

            result.Append($"<div id='list_{formattedNodePath}' style='display: none;'>");

            if (node.Sensors != null && !node.Sensors.IsEmpty)
            {
                foreach (var (name, sensor) in node.Sensors)
                {
                    string sensorPath = $"{node.Path}/{name}";
                    string formattedPath = SensorPathHelper.Encode(sensorPath);
                    result.Append($"<div id='sensorInfo_parent_{formattedPath}' style='display: none'>");
                    result.Append(CreateSensorInfoLink(formattedPath));
                    result.Append($"<div id=sensor_info_{formattedPath}></div></div>");
                    result.Append($"<div class='accordion' id='sensorData_{formattedPath}' style='display: none'>");
                    result.Append(CreateSensor(formattedPath, sensor));
                    result.Append("</div>");
                }
            }

            result.Append("</div>");
            return result;
        }

        public static string CreateSensorInfoLink(string formattedPath)
        {
            return $"<a tabindex='0' class='link-primary info-link' id='sensorInfo_link_{formattedPath}'>Show meta info</a>";
        }

        public static StringBuilder CreateSensor(string formattedPath, SensorViewModel sensor)
        {
            var result = new StringBuilder(1 << 5);

            string name = formattedPath;

            result.Append("<div class='accordion-item'>")
                  .Append($"<h2 class='accordion-header' id='heading_{name}'>");

            var time = (DateTime.UtcNow - sensor.Time);

            if (sensor.SensorType == SensorType.FileSensorBytes)
            {
                //header
                string fileName = GetFileNameString(sensor.StringValue);

                //button
                result.Append($"<button id='{name}' class='accordion-button' style='display: none' type='button' data-bs-toggle='collapse'")
                      .Append($"data-bs-target='#collapse_{name}' aria-expanded='true' aria-controls='collapse_{name}'>")
                      .Append($"<div><div class='row'><div class='col-md-auto'>{sensor.Name}</div>")
                      .Append($"<div class='col'>{sensor.StringValue}</div></div></div></button></h2>");
                //body
                result.Append($"<div id='collapse_{name}' class='accordion-collapse' ")
                      .Append($"aria-labelledby='heading_{name}' data-bs-parent='#sensorData_{formattedPath}'>")
                      .Append("<div class='accordion-body'>");

                result.Append("<div style='width: 100%'><div class='row justify-content-between'><div class='col-md-auto'>")
                      .Append($"<li id='status_{name}' class='fas fa-circle sensor-icon-with-margin ")
                      .Append($"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>");

                result.Append($"<span id='validation_{name}'>");
                if (!string.IsNullOrEmpty(sensor.ValidationError))
                {
                    result.Append(CreateValidationErrorIcon(sensor.ValidationError, name));
                }
                result.Append("</span>");

                result.Append($"{sensor.Name}</div><input id='sensor_type_{name}' value='{(int)sensor.SensorType}' ")
                      .Append($"style='display: none' /><div class='col-md-auto time-ago-div' id='update_{name}' ")
                      .Append($"style='margin-right: 10px'>updated {GetTimeAgo(time)}</div></div>{sensor.ShortStringValue}</div>")
                      .Append($"<div class='row'><div class='col-md-auto'><button id='button_view_{name}' ")
                      .Append("class='button-view-file-sensor btn btn-secondary' title='View'>")
                      .Append($"<i class='fas fa-eye'></i></button></div><div class='col'><input style='display: none;'")
                      .Append($" id='fileType_{name}' value='{fileName}'><button id='button_download_{name}'")
                      .Append(" class='button-download-file-sensor-value btn btn-secondary'")
                      .Append(" title='Download'><i class='fas fa-file-download'></i></button></div></div>");


                result.Append("</div></div></div>");

                return result;
            }

            result.Append($"<button id='{name}' class='accordion-button collapsed' type='button' data-bs-toggle='collapse'")
                  .Append($"data-bs-target='#collapse_{name}' aria-expanded='false' aria-controls='collapse_{name}'>")
                  .Append("<div style='width: 100%'><div class='row justify-content-between'>")
                  .Append($"<div class='col-md-auto'><li id='status_{name}' class='fas fa-circle sensor-icon-with-margin ")
                  .Append($"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>");

            result.Append($"<span id='validation_{name}'>");
            if (!string.IsNullOrEmpty(sensor.ValidationError))
            {
                result.Append(CreateValidationErrorIcon(sensor.ValidationError, name));
            }
            result.Append("</span>");

            result.Append($"{sensor.Name}</div><div class='col-md-auto'>")
                  .Append($"<input id='sensor_type_{name}' value='{(int)sensor.SensorType}' style='display: none' />")
                  .Append($"<div id='update_{name}' class='time-ago-div' style='margin-right: 10px'>updated {GetTimeAgo(time)}</div></div></div>")
                  .Append($"<div id='value_{name}'>{sensor.ShortStringValue}</div></div></button></h2>");

            result.Append($"<div id='collapse_{name}' class='accordion-collapse collapse'")
                  .Append($"aria-labelledby='heading_{name}' data-bs-parent='#sensorData_{formattedPath}'>")
                  .Append($"<div class='accordion-body'><input style='display: none' id='listId_{name}' value='{formattedPath}'/>")
                  .Append("<div class='mb-3 row'><div>")
                  .Append(CreateRadioButton(name, "hour", "1H"))
                  .Append(CreateRadioButton(name, "day", "1D"))
                  .Append(CreateRadioButton(name, "three_days", "3D"))
                  .Append(CreateRadioButton(name, "week", "1W"))
                  .Append(CreateRadioButton(name, "month", "1M"))
                  .Append(CreateRadioButton(name, "all", "All"))
                  .Append(CreateActionsList(name)).Append("</div>");

            result.Append("<div style='margin-top: 15px'>");
            result.Append(GetNoDataDivForSensor(name));
            result.Append($"<div id='history_{name}'>");
            result.Append(IsPlottingSupported(sensor.SensorType)
                            ? GetNavTabsForHistory(name)
                            : GetValuesDivForHistory(name));

            result.Append("</div></div></div></div></div></div>");

            return result;
        }

        private static string CreateValidationErrorIcon(string validationError, string name)
        {
            return $"<li id='errorIcon_{name}' class='fas fa-exclamation-triangle' style='margin-right:5px'" +
                          $" title='{validationError}'></li>";
        }
        public static string GetTimeAgo(TimeSpan time)
        {
            if (time.TotalDays > 30)
                return "> a month ago";

            if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";

            if (time.TotalHours >= 1)
            {
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";
            }

            if (time.TotalMinutes >= 1)
            {
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";
            }

            if (time.TotalSeconds < 60)
                return "< 1 minute ago";

            return "no info";
        }

        private static string CreateActionsList(string name)
        {
            return "<div class='btn-group'>" +
                "<button class='btn btn-secondary btn-sm dropdown-toggle' type='button'" +
                "data-bs-toggle='dropdown'>Actions</button>" +
                "<ul class='dropdown-menu'>" +
                $"<li><a class='dropdown-item' href='#' id='button_delete_sensor_{name}'>Delete sensor</a></li>" +
                $"<li><a class='dropdown-item' href='#' id='button_export_csv_{name}'>Export to CSV</a></li>" +
                "</ul></div>";
        }

        private static string CreateRadioButton(string name, string period, string shortPeriod)
        {
            return
                $"<div class='form-check form-check-inline'><input class='form-check-input' type='radio' name='group_{name}' " +
                $"id='radio_{period}_{name}' /><label class='form-check-label' for='{period}_radio_{name}'>{shortPeriod}</label></div>";
        }
        private static string UnitsToString(double value, string unit)
        {
            int intValue = Convert.ToInt32(value);
            return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
        }

        private static string GetNoDataDivForSensor(string name)
        {
            return $"<div id='no_data_{name}' style='display: none'><p>No data for the specified period</p></div>";
        }
        private static string GetNavTabsForHistory(string name)
        {
            var sb = new StringBuilder(1 << 3);
            sb.Append("<ul class='nav nav-tabs'>");

            //Graph tab
            string graphElementId = $"graph_{name}";
            string graphParentDivId = $"graph_parent_{name}";
            sb.Append($"<li class='nav-item'><a id='link_graph_{name}' ")
              .Append($"class='nav-link active' data-bs-toggle='tab' href='#{graphParentDivId}'>Graph</a></li>");

            //Values tab
            string valuesElementId = $"values_{name}";
            string valuesParentDivId = $"values_parent_{name}";
            sb.Append($"<li class='nav-item'><a id='link_table_{name}' ")
              .Append($"class='nav-link' data-bs-toggle='tab' href='#{valuesParentDivId}'>Table</a></li></ul>");

            sb.Append("<div class='tab-content'>");
            sb.Append($"<div class='tab-pane fade show active' id={graphParentDivId}><div id='{graphElementId}'></div></div>");
            sb.Append($"<div class='tab-pane fade' id={valuesParentDivId}><div id='{valuesElementId}'></div></div></div>");

            return sb.ToString();
        }

        private static string GetValuesDivForHistory(string name)
        {
            return $"<div id='values_{name}'></div>";
        }

        private static bool IsPlottingSupported(SensorType sensorType)
        {
            if (sensorType == SensorType.IntSensor || sensorType == SensorType.DoubleSensor)
                return true;

            if (sensorType == SensorType.DoubleBarSensor || sensorType == SensorType.IntegerBarSensor)
                return true;

            if (sensorType == SensorType.BooleanSensor)
                return true;

            return false;
        }

        private static string GetFileNameString(string shortValue)
        {
            var ind = shortValue.IndexOf(FileNamePattern);
            if (ind != -1)
            {
                var fileNameString = shortValue[(ind + FileNamePattern.Length)..];
                int firstDotIndex = fileNameString.IndexOf('.');
                int secondDotIndex = fileNameString[(firstDotIndex + 1)..].IndexOf('.');
                return fileNameString[..(firstDotIndex + secondDotIndex + 1)];
            }

            ind = shortValue.IndexOf(ExtensionPattern);
            if (ind != -1)
            {
                var extensionString = shortValue[(ind + ExtensionPattern.Length)..];
                int dotIndex = extensionString.IndexOf('.');
                return extensionString[..dotIndex];
            }

            return string.Empty;
        }

        public static string CreateHistoryList(List<SensorHistoryData> sensors)
        {
            if (sensors == null)
                return string.Empty;

            var result = new StringBuilder(sensors.Count + 2);
            result.Append("<div class='col-xxl' style='margin: 10px 0px'><ul class='list-group'>");

            foreach (var sensor in sensors)
            {
                result.Append("<li class='list-group-item list-group-item-action'>")
                      .Append($"{sensor.Time}: {sensor.TypedData}").Append("</li>");
            }

            result.Append("</ul></div>");
            return result.ToString();
        }

        private static NodeViewModel GetNodeRecursion(string path, NodeViewModel model)
        {
            var nodes = path.Split('/');

            if (nodes[0].Length == path.Length)
                return model.Nodes[nodes[0]];

            path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
            var existingNode = model.Nodes[nodes[0]];

            return GetNodeRecursion(path, existingNode);
        }
    }
}
