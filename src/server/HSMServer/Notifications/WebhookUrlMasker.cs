using System;

namespace HSMServer.Notifications
{
    // Masks webhook URLs for display in EditChat so the cleartext secret is never rendered to the
    // browser. The mask keeps scheme + host + all path segments except the LAST one verbatim; the
    // last segment (typically the secret token) is reduced to its first 4 and last 4 chars with the
    // marker in between — e.g. `http://localhost:8065/hooks/nuddbzeoztbau8juhobap348fw` becomes
    // `http://localhost:8065/hooks/nudd••••48fw`, so an admin can recognize the webhook without the
    // full token being recoverable.
    //
    // The marker doubles as the POST sentinel: EditChat posts the masked value back when the admin
    // didn't replace the webhook, and ChatViewModel.ToUpdate treats any IsMasked value as "no change"
    // (null in the ChatUpdate → Chat.ApplyUpdate keeps the stored URL).
    public static class WebhookUrlMasker
    {
        public const string MaskMarker = "••••";

        // Visible head/tail of the last path segment. Tuned so an admin can tell two webhooks apart
        // without leaking enough to reconstruct the secret token.
        private const int LastSegmentHeadChars = 4;
        private const int LastSegmentTailChars = 4;
        private const int LastSegmentRevealThreshold = LastSegmentHeadChars + LastSegmentTailChars;

        // null/empty/whitespace → null (distinguishes "no webhook" from "masked webhook" and lets
        // ToUpdate apply the ??-no-change rule uniformly with the empty-input case).
        public static string Mask(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Find the last '/' that is NOT part of `scheme://`. Without this guard, a URL with no
            // real path (`https://hooks.slack.com`) would mask the host itself. `schemeIndex` points
            // at the second slash of `://`; only slashes after it count as path separators.
            var schemeIndex = url.IndexOf("://", StringComparison.Ordinal);
            var searchFrom = schemeIndex >= 0 ? schemeIndex + 3 : 0;
            var lastSlash = url.LastIndexOf('/');

            if (lastSlash < searchFrom)
                return url + MaskMarker;

            var head = url.Substring(0, lastSlash + 1); // includes the trailing '/'
            var lastSegment = url.Substring(lastSlash + 1);
            return head + MaskLastSegment(lastSegment);
        }

        // True iff url carries the mask marker — i.e. it's a value we (or someone typing the literal
        // bullets) emitted, not a real webhook URL. Real URLs never contain `••••`.
        public static bool IsMasked(string url) => url != null && url.Contains(MaskMarker);

        // Reduces the last path segment to `head4 + MaskMarker + tail4`. A short segment (≤ 8 chars)
        // is shown verbatim per UX decision, with the marker appended as a trailing sentinel so
        // IsMasked stays true for every masked value (without it, the POST "no change" detection in
        // ToUpdate would treat the round-tripped value as a fresh URL and overwrite the stored one).
        private static string MaskLastSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return MaskMarker;

            if (segment.Length <= LastSegmentRevealThreshold)
                return segment + MaskMarker;

            var head = segment.Substring(0, LastSegmentHeadChars);
            var tail = segment.Substring(segment.Length - LastSegmentTailChars);
            return head + MaskMarker + tail;
        }
    }
}
