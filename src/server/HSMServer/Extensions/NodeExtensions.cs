using HSMServer.Model.TreeViewModel;

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
                _ => "tree-icon-offTime",
            };

        internal static string ToIcon(this SensorStatus status) =>
            $"fas fa-circle {status.ToCssIconClass()}";

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

        internal static string GetShortNodeName(this string name) => name.Cut(NodeNameMaxLength);

        internal static string GetShortCellName(this string name) => name.Cut(CellNameMaxLength);

        private static string Cut(this string str, int stringLength) =>
            str.Length > stringLength ? $"{str[..stringLength]}..." : str;
    }
}
