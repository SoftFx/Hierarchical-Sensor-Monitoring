using HSMServer.Core.Model;

namespace HSMServer.Extensions;

public static class AccessKeyExtensions
{
    internal static string ToCssIconClass(this KeyState state) =>
        state switch
        {
            KeyState.Active => "check key-icon-active",
            KeyState.Expired => "exclamation key-icon-expired",
            KeyState.Blocked => "xmark key-icon-blocked",
            _ => "dot",
        };

    internal static string ToIcon(this KeyState state) =>
        $"fa-regular fa-circle-{state.ToCssIconClass()}";
}