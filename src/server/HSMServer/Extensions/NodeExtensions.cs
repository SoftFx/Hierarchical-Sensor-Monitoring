using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications;
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


        internal static Dictionary<Guid, string> GetAvailableChats(this BaseNodeViewModel node, ITelegramChatsManager chatsManager)
        {
            node.TryGetChats(out var folderChats);

            return GetAvailableChats(folderChats, chatsManager).ToDictionary(k => k.Id, v => v.Name);
        }

        internal static List<TelegramChat> GetAvailableChats(this HashSet<Guid> folderChats, ITelegramChatsManager chatsManager)
        {
            var availableChats = new List<TelegramChat>(1 << 3);

            foreach (var chat in chatsManager.GetValues())
                if (folderChats.Contains(chat.Id))
                    availableChats.Add(chat);

            return availableChats;
        }

        internal static bool TryGetChats(this BaseNodeViewModel model, out HashSet<Guid> chats)
        {
            if (model is FolderModel folder)
            {
                chats = folder.TelegramChats;
                return true;
            }
            else if (model is NodeViewModel node && node.RootProduct.Parent is FolderModel rootFolder)
            {
                chats = rootFolder.TelegramChats;
                return true;
            }

            chats = [];
            return false;
        }


        internal static bool HasUnconfiguredAlerts(this SensorNodeViewModel sensor)
        {
            bool IsUnconfigured(DataAlertViewModelBase alert) =>
                !alert.IsDisabled && alert.IsNotInitializedDestination(sensor);


            return sensor.HasData && sensor.State is not Core.Model.SensorState.Muted &&
                   (sensor.DataAlerts.Values.Any(d => d.Any(a => IsUnconfigured(a))) || (!sensor.TTL.IsIntervalNone && IsUnconfigured(sensor.TTLAlert)));
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
