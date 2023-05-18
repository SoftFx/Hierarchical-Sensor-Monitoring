using HSMServer.Core.Model;

namespace HSMServer.Extensions;

public static class AccessKeyExtensions
{
    internal static string ToCssIconClass(this KeyState state) =>
        state switch
        {
            KeyState.Active => "fa-regular fa-circle-check key-icon-active",
            KeyState.Expired => "fa fa-exclamation-circle key-icon-expired",
            KeyState.Blocked => "fa-regular fa-circle-xmark key-icon-blocked",
            _ => "fa-regular fa-circle-dot",
        };

    internal static string ToIcon(this KeyState state) =>
        $"{state.ToCssIconClass()}";
}