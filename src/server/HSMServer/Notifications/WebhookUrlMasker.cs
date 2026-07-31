using System;

namespace HSMServer.Notifications
{
    // Masks webhook URLs for display in EditChat so the cleartext secret is never rendered to the
    // browser. The mask keeps scheme + host + the first path segment (enough for an admin to tell
    // `…/services/…` from `…/hooks/…`) and replaces the rest with the fixed marker `••••`.
    //
    // The marker doubles as the POST sentinel: EditChat posts the masked value back when the admin
    // didn't replace the webhook, and ChatViewModel.ToUpdate treats any IsMasked value as "no change"
    // (null in the ChatUpdate → Chat.ApplyUpdate keeps the stored URL).
    public static class WebhookUrlMasker
    {
        public const string MaskMarker = "••••";

        // null/empty/whitespace → null (distinguishes "no webhook" from "masked webhook" and lets
        // ToUpdate apply the ??-no-change rule uniformly with the empty-input case).
        public static string Mask(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                var host = uri.Scheme + Uri.SchemeDelimiter + uri.Host;
                var firstSegment = GetFirstPathSegment(uri.AbsolutePath);
                return host + firstSegment + "/" + MaskMarker;
            }

            // Unparseable input (rare for a stored webhook) — still mask rather than throw. Fall back
            // to slicing at the first '/' after the host-ish portion so we never leak the tail.
            var firstSlash = url.IndexOf('/', StringComparison.Ordinal);
            return firstSlash < 0 ? url + "/" + MaskMarker : url.Substring(0, firstSlash) + "/" + MaskMarker;
        }

        // True iff url carries the mask marker — i.e. it's a value we (or someone typing the literal
        // bullets) emitted, not a real webhook URL. Real URLs never contain `••••`.
        public static bool IsMasked(string url) => url != null && url.Contains(MaskMarker);

        // Returns "/segment" (with a leading slash) for the first non-empty path piece, or "" when the
        // URL has no path. Trailing slashes on the host root are treated as "no path".
        private static string GetFirstPathSegment(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return string.Empty;

            var segments = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 0 ? string.Empty : "/" + segments[0];
        }
    }
}
