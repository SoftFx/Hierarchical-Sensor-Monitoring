using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.UserFilters;
using Microsoft.AspNetCore.Html;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class NodeExtensions
    {
        private const int NodeNameMaxLength = 35;
        private const int CellNameMaxLength = 13;


        internal static string ToCssIconClass(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => "tree-icon-ok",
                SensorStatus.Warning => "tree-icon-warning",
                SensorStatus.Error => "tree-icon-error",
                SensorStatus.Empty => GetEmptySensorIcon(),
                _ => "tree-icon-offTime",
            };

        internal static SensorStatus ToEmpty(this SensorStatus status, bool hasData) =>
            !hasData ? SensorStatus.Empty : status;

        internal static string ToIcon(this SensorStatus status) =>
            $"fas fa-circle {status.ToCssIconClass()}";

        internal static string ToIcon(this string icon) =>
            icon switch
            {
                "↕️" => "fa-solid fa-arrows-up-down",
                _ => string.Empty,
            };

        internal static HtmlString ToIconStatus(this SensorStatus status) =>
            new($"<span class='{status.ToIcon()}'></span> {status}");

        internal static string ToCssClass(this Core.Model.SensorState state) =>
            state switch
            {
                Core.Model.SensorState.Muted => "muted-state-text",
                _ => string.Empty,
            };

        internal static string ToCssGridCellClass(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => "grid-cell-ok",
                SensorStatus.Warning => "grid-cell-warning",
                SensorStatus.Error => "grid-cell-error",
                _ => "grid-cell-offTime",
            };

        internal static IOrderedEnumerable<T> GetOrdered<T>(this IEnumerable<T> collection, User user) where T : BaseNodeViewModel =>
            user.TreeFilter.TreeSortType switch
            {
                TreeSortType.ByTime => collection.OrderByDescending(x => x.UpdateTime),
                _ => collection.OrderBy(x => x.Name)
            };

        internal static string GetEmptySensorIcon() => "fa-regular fa-circle";

        internal static string GetShortNodeName(this string name) => name.Cut(NodeNameMaxLength);

        internal static string GetShortCellName(this string name) => name.Cut(CellNameMaxLength);

        private static string Cut(this string str, int stringLength) =>
            str.Length > stringLength ? $"{str[..stringLength]}..." : str;
    }
}
