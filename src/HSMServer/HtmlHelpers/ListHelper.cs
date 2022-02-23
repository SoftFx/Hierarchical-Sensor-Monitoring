﻿using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Helpers;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

            result.Append("<div class='col' id='list'>" +
                "<div id='list_sensors_header' style='display: none;'>" +
                "<h5 style='margin: 0px 20px 10px;'>Sensors</h5></div>");

            result.Append("<div style='width: 800px'>" +
                "<ul id='noData' class='list-group'>" +
                "<li class='list-group-item'>No Data</li></ul></div>");

            foreach (var path in model.Paths)
            {
                result.Append(CreateList(path, path, model));
            }
            result.Append("</div>");

            return result.ToString();
        }

        public static string CreateNotSelectedLists(string selectedPath, TreeViewModel model)
        {
            if (model == null) return string.Empty;

            StringBuilder result = new StringBuilder();

            foreach(var path in model.Paths)
            {
                string formattedPath = SensorPathHelper.Encode(path);
                if (!string.IsNullOrEmpty(selectedPath) 
                    && selectedPath.Equals(formattedPath)) continue;

                result.Append(CreateList(path, path, model));
            }

            return result.ToString();
        }

        public static string CreateList(string path, string fullPath, TreeViewModel model)
        {
            if (path == null) return string.Empty;

            var nodes = path.Split('/');
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));
            NodeViewModel node = existingNode;
            if (nodes[0].Length < path.Length)
            {
                //Remove last node name because it is sensor
                //if (nodes.Length > 2)
                //{
                //    path = path.Substring(nodes[0].Length + 1,
                //        path.Length - nodes[0].Length - 1 - nodes.Last().Length - 1);
                //    node = GetNodeRecursion(path, existingNode);
                //}
                path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
                node = GetNodeRecursion(path, existingNode);
            }

            StringBuilder result = new StringBuilder();
            string formattedNodePath = SensorPathHelper.Encode(fullPath);

            result.Append($"<div id='list_{formattedNodePath}' style='display: none;'>");
            //if (node.Sensors != null)
            //    foreach (var sensor in node.Sensors)
            //    {
            //        result.Append(CreateSensor(fullPath, sensor));
            //    }
            //result.Append("</div>");
            //string shortedPath = fullPath.Substring(0, fullPath.LastIndexOf('/'));
            if (node.Sensors != null && node.Sensors.Any())
            {
                foreach (var sensor in node.Sensors)
                {
                    string sensorPath = $"{fullPath}/{sensor.Name}";
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
            return result.ToString();
        }

        public static string CreateSensorInfoLink(string formattedPath)
        {
            return $"<a tabindex='0' class='link-primary info-link' id='sensorInfo_link_{formattedPath}'>Show meta info</a>";
        }
        public static StringBuilder CreateSensor(string formattedPath, SensorViewModel sensor)
        {
            StringBuilder result = new StringBuilder();
            //string name = SensorPathHelper.Encode($"{path}/{sensor.Name}");
            //string formattedPath = SensorPathHelper.Encode(path);
            string name = formattedPath;

            result.Append("<div class='accordion-item'>" +
                          $"<h2 class='accordion-header' id='heading_{name}'>");

            var time = (DateTime.UtcNow - sensor.Time);

            if (sensor.SensorType == SensorType.FileSensorBytes)
            {
                //header
                string fileName = GetFileNameString(sensor.StringValue);

                //button
                result.Append($"<button id='{name}' class='accordion-button' style='display: none' type='button' data-bs-toggle='collapse'" +
                          $"data-bs-target='#collapse_{name}' aria-expanded='true' aria-controls='collapse_{name}'>" +
                          "<div>" +
                          $"<div class='row'><div class='col-md-auto'>{sensor.Name}</div>" +
                          $"<div class='col'>{sensor.StringValue}</div></div></div></button></h2>");
                //body
                result.Append($"<div id='collapse_{name}' class='accordion-collapse' " +
                          $"aria-labelledby='heading_{name}' data-bs-parent='#sensorData_{formattedPath}'>" +
                          "<div class='accordion-body'>");

                result.Append("<div style='width: 100%'>" +
                              "<div class='row justify-content-between'><div class='col-md-auto'>" +
                              $"<li id='status_{name}' class='fas fa-circle sensor-icon-with-margin " +
                              $"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>");

                result.Append($"<span id='validation_{name}'>");
                if (!string.IsNullOrEmpty(sensor.ValidationError))
                {
                    result.Append(CreateValidationErrorIcon(sensor.ValidationError, name));
                }

                result.Append("</span>");
                result.Append($"{sensor.Name}</div><input id='sensor_type_{name}' value='{(int)sensor.SensorType}' " +
                              $"style='display: none' /><div class='col-md-auto time-ago-div' id='update_{name}' " +
                              $"style='margin-right: 10px'>updated {GetTimeAgo(time)}</div></div>{sensor.ShortStringValue}</div>" +
                              $"<div class='row'><div class='col-md-auto'><button id='button_view_{name}' " +
                              "class='button-view-file-sensor btn btn-secondary' title='View'>" +
                              $"<i class='fas fa-eye'></i></button></div><div class='col'><input style='display: none;'" +
                              $" id='fileType_{name}' value='{fileName}'><button id='button_download_{name}'" +
                              " class='button-download-file-sensor-value btn btn-secondary'" +
                              " title='Download'><i class='fas fa-file-download'></i></button></div></div>");


                result.Append("</div></div></div>");

                return result;
            }

            result.Append(
                $"<button id='{name}' class='accordion-button collapsed' type='button' data-bs-toggle='collapse'" +
                $"data-bs-target='#collapse_{name}' aria-expanded='false' aria-controls='collapse_{name}'>" +
                "<div style='width: 100%'>" +
                "<div class='row justify-content-between'>" +
                $"<div class='col-md-auto'><li id='status_{name}' class='fas fa-circle sensor-icon-with-margin " +
                $"{ViewHelper.GetStatusHeaderColorClass(sensor.Status)}' title='Status: {sensor.Status}'></li>");

            result.Append($"<span id='validation_{name}'>");
            if (!string.IsNullOrEmpty(sensor.ValidationError))
            {
                result.Append(CreateValidationErrorIcon(sensor.ValidationError, name));
            }

            result.Append("</span>");
            result.Append($"{sensor.Name}</div><div class='col-md-auto'>" +
                          $"<input id='sensor_type_{name}' value='{(int)sensor.SensorType}' style='display: none' />" +
                          $"<div id='update_{name}' class='time-ago-div' style='margin-right: 10px'>updated {GetTimeAgo(time)}</div></div></div>" +
                          $"<div id='value_{name}'>{sensor.ShortStringValue}</div></div></button></h2>");

            result.Append($"<div id='collapse_{name}' class='accordion-collapse collapse'" +
                          $"aria-labelledby='heading_{name}' data-bs-parent='#sensorData_{formattedPath}'>" +
                          $"<div class='accordion-body'><input style='display: none' id='listId_{name}' value='{formattedPath}'/>" +
                          "<div class='mb-3 row'><div>" +
                          CreateRadioButton(name, "hour", "1H") +
                          CreateRadioButton(name, "day", "1D") +
                          CreateRadioButton(name, "three_days", "3D") +
                          CreateRadioButton(name, "week", "1W") +
                          CreateRadioButton(name, "month", "1M") + 
                          CreateRadioButton(name, "all", "All") + 
                          //CreateCsvButton(name) +
                          CreateActionsList(name) + "</div>");

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
                //return $"{time:%d} day(s) {time:%h} hours {time:%m} minutes";
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

        private static string CreateCsvButton(string name)
        {
            return "<div class='form-check form-check-inline'><button type='button'" +
                   $" class='btn btn-primary' id='button_export_csv_{name}'>Export to CSV</button></div>";
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
            StringBuilder sb = new StringBuilder();
            sb.Append("<ul class='nav nav-tabs'>");

            //Graph tab
            string graphElementId = $"graph_{name}";
            string graphParentDivId = $"graph_parent_{name}";
            sb.Append($"<li class='nav-item'><a id='link_graph_{name}' " +
                $"class='nav-link active' data-bs-toggle='tab' href='#{graphParentDivId}'>Graph</a></li>");

            //Values tab
            string valuesElementId = $"values_{name}";
            string valuesParentDivId = $"values_parent_{name}";
            sb.Append($"<li class='nav-item'><a id='link_table_{name}' " +
                $"class='nav-link' data-bs-toggle='tab' href='#{valuesParentDivId}'>Table</a></li></ul>");

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
            var nodes = path.Split('/');

            if (nodes[0].Length == path.Length)
                return model.Nodes.First(x => x.Name.Equals(nodes[0]));

            path = path.Substring(nodes[0].Length + 1, path.Length - nodes[0].Length - 1);
            var existingNode = model.Nodes.First(x => x.Name.Equals(nodes[0]));

            return GetNodeRecursion(path, existingNode);
        }
    }
}
