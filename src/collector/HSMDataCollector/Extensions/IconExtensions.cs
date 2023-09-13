using HSMDataCollector.Alerts;

namespace HSMDataCollector.Extensions
{
    internal static class IconExtensions
    {
        public static string ToUtf8(this AlertIcon icon)
        {
            switch (icon)
            {
                case AlertIcon.Ok:
                    return "✅";
                case AlertIcon.Warning:
                    return "⚠";
                case AlertIcon.Error:
                    return "❌";
                case AlertIcon.Pause:
                    return "⏸";
                case AlertIcon.ArrowUp:
                    return "⬆";
                case AlertIcon.ArrowDown:
                    return "⬇";
                case AlertIcon.Clock:
                    return "🕐";
                case AlertIcon.Hourglass:
                    return "⌛";
                default:
                    return string.Empty;
            }
        }
    }
}
