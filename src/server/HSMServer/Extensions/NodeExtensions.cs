using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Telegram;
using HSMServer.UserFilters;
using Microsoft.AspNetCore.Html;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class NodeExtensions
    {
        private const int NodeNameMaxLength = 35;
        private const int CellNameMaxLength = 13;
        private const int IconSize = 3;


        internal static List<TelegramChat> GetAllChats(this NodeViewModel node)
        {
            var availableGroups = node.RootProduct.Notifications.Telegram.Chats.Values;
            var availableUsers = node.RootProduct.GetAllUserChats().Values;

            return availableGroups.Union(availableUsers).OrderBy(chat => chat.IsUserChat).ThenBy(chat => chat.Name).ToList();
        }


        internal static string ToCssIconClass(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => "tree-icon-ok",
                SensorStatus.Error => "tree-icon-error",
                SensorStatus.Empty => GetEmptySensorIcon(),
                _ => "tree-icon-offTime",
            };

        internal static string ToIcon(this SensorStatus status) =>
            $"fas fa-circle {status.ToCssIconClass()}";

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
                SensorStatus.Error => "grid-cell-error",
                SensorStatus.OffTime => "grid-cell-offTime",
                _ => "grid-cell-empty",
            };

        internal static IOrderedEnumerable<T> GetOrdered<T>(this IEnumerable<T> collection, User user) where T : BaseNodeViewModel =>
            user.TreeFilter.TreeSortType switch
            {
                TreeSortType.ByTime => collection.OrderByDescending(x => x.UpdateTime),
                _ => collection.OrderBy(x => x.Name)
            };

        internal static string GetEmptySensorIcon() => "fa-regular fa-circle";

        internal static string GetShortNodeName(this string name) => name.Cut(NodeNameMaxLength);

        internal static string GetShortCellName(this string name, int iconsLengthDifference = 0) => name.Cut(CellNameMaxLength - iconsLengthDifference.GetIconsLength());

        private static string Cut(this string str, int stringLength) =>
            str.Length > stringLength ? $"{str[..stringLength]}..." : str;

        private static int GetIconsLength(this int iconsCount) => Math.Min(iconsCount, AlertIconsViewModel.VisibleMaxSize) * IconSize;
    }
}
