using HSMServer.Core.Model;

namespace HSMServer.Extensions;

public static class AccessKeyExtensions
{
    internal static string ToCssIconClass(this KeyState status) =>
        status switch
        {
            KeyState.Active => "tree-icon-ok",
            KeyState.Expired => "tree-icon-warning",
            KeyState.Blocked => "tree-icon-error",
            _ => "tree-icon-offTime",
        };

    internal static string ToIcon(this KeyState status) =>
        $"fas fa-circle {status.ToCssIconClass()}";
}