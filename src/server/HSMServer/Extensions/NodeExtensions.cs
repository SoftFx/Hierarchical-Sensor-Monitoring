using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModels;
using System;

namespace HSMServer.Extensions
{
    public static class NodeExtensions
    {
        private const int NodeNameMaxLength = 35;
        private const int CellNameMaxLength = 10;


        internal static string ToCssIconClass(this SensorStatusWeb status) =>
            status switch
            {
                SensorStatusWeb.Ok => "tree-icon-ok",
                SensorStatusWeb.Warning => "tree-icon-warning",
                SensorStatusWeb.Error => "tree-icon-error",
                _ => "tree-icon-offTime",
            };

        internal static string ToIcon(this SensorStatusWeb status) =>
            $"fas fa-circle {status.ToCssIconClass()}";

        internal static string ToCssClass(this SensorState state) =>
            state switch
            {
                SensorState.Blocked => "blockedSensor-span",
                _ => string.Empty,
            };

        internal static string ToCssGridCellClass(this SensorStatusWeb status) =>
            status switch
            {
                SensorStatusWeb.Ok => "grid-cell-ok",
                SensorStatusWeb.Warning => "grid-cell-warning",
                SensorStatusWeb.Error => "grid-cell-error",
                _ => "grid-cell-offTime",
            };


        internal static string GetTimeAgo(this NodeViewModel node)
        {
            string UnitsToString(double value, string unit)
            {
                int intValue = Convert.ToInt32(value);
                return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
            }


            var time = node.UpdateTime != DateTime.MinValue ? DateTime.UtcNow - node.UpdateTime : TimeSpan.MinValue;

            if (time == TimeSpan.MinValue)
                return " - no data";
            else if (time.TotalDays > 30)
                return "> a month ago";
            else if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";
            else if (time.TotalHours >= 1)
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";
            else if (time.TotalMinutes >= 1)
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";
            else if (time.TotalSeconds < 60)
                return "< 1 minute ago";

            return "no info";
        }


        internal static string GetShortNodeName(this string name) => name.Cut(NodeNameMaxLength);

        internal static string GetShortCellName(this string name) => name.Cut(CellNameMaxLength);

        private static string Cut(this string str, int stringLength) =>
            str.Length > stringLength ? $"{str[..stringLength]}..." : str;
    }
}
