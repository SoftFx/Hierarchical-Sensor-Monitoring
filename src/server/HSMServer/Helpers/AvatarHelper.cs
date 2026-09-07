using System;

namespace HSMServer.Helpers
{
    // Deterministic avatar colour for a user id, shared by the header user menu, the
    // profile card and the Users page so one user is always the same colour everywhere.
    // The hash input is the user ID (a Guid with a process-stable GetHashCode), never
    // the name: String.GetHashCode is randomised per process on .NET Core, which would
    // change the colour on every restart.
    public static class AvatarHelper
    {
        private static readonly string[] Colors =
        [
            "#9ca3af", "#8b95b0", "#7a87c5", "#6979da", "#586bef", "#4659f5", "#3b4de0", "#2d3fc7",
        ];


        public static string ColorOf(Guid userId) =>
            Colors[(userId.GetHashCode() & 0x7FFFFFFF) % Colors.Length];


        public static string InitialsOf(string userName)
        {
            var trimmed = (userName ?? string.Empty).Trim();

            return trimmed.Length >= 2 ? trimmed.Substring(0, 2).ToUpperInvariant() : trimmed.ToUpperInvariant();
        }
    }
}
