using System;

namespace HSMServer.Notifications
{
    // Masks webhook URLs for display in EditChat so the cleartext secret is never rendered to the
    // browser. The mask keeps scheme + host + a prefix of the path + the last few chars of the URL,
    // so an admin can still recognize which webhook is set (`…/services/T01••••cret` is distinct
    // from `…/services/T02••••ging`) without the full secret being recoverable.
    //
    // The marker doubles as the POST sentinel: EditChat posts the masked value back when the admin
    // didn't replace the webhook, and ChatViewModel.ToUpdate treats any IsMasked value as "no change"
    // (null in the ChatUpdate → Chat.ApplyUpdate keeps the stored URL).
    public static class WebhookUrlMasker
    {
        public const string MaskMarker = "••••";

        // Visible chars of the path prefix / URL tail. Tuned so an admin can tell two webhooks apart
        // without leaking enough to reconstruct the secret.
        private const int PathPrefixChars = 8;
        private const int TailChars = 4;

        // null/empty/whitespace → null (distinguishes "no webhook" from "masked webhook" and lets
        // ToUpdate apply the ??-no-change rule uniformly with the empty-input case).
        public static string Mask(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                var host = uri.Scheme + Uri.SchemeDelimiter + uri.Host;
                var pathAndQuery = uri.PathAndQuery;
                return host + MaskMiddle(pathAndQuery);
            }

            // Unparseable input (rare for a stored webhook) — still mask rather than throw. Slice at
            // the first '/' so the host-ish prefix survives and the tail is masked the same way.
            var firstSlash = url.IndexOf('/', StringComparison.Ordinal);
            var head = firstSlash < 0 ? url : url.Substring(0, firstSlash);
            var rest = firstSlash < 0 ? string.Empty : url.Substring(firstSlash);
            return head + MaskMiddle(rest);
        }

        // True iff url carries the mask marker — i.e. it's a value we (or someone typing the literal
        // bullets) emitted, not a real webhook URL. Real URLs never contain `••••`.
        public static bool IsMasked(string url) => url != null && url.Contains(MaskMarker);

        // Keeps the first PathPrefixChars and the last TailChars of `body`, inserting the marker
        // between. A short body (≤ prefix+tail threshold) is replaced by the marker alone — showing
        // it verbatim would leak the whole short secret, and the marker MUST be present so the POST
        // sentinel detection in IsMasked works for every masked value.
        private static string MaskMiddle(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length <= PathPrefixChars + TailChars)
                return "/" + MaskMarker;

            var prefix = body.Substring(0, PathPrefixChars);
            var tail = body.Substring(body.Length - TailChars);
            return prefix + MaskMarker + tail;
        }
    }
}

