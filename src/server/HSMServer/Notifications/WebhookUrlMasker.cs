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
    //
    // Masking is segment-based via Uri parsing, NOT raw string slicing, so trailing slashes and
    // query strings don't move the split point past the secret token (regression coverage lives in
    // WebhookUrlMaskerTests).
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

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile)
                return MaskAbsoluteUri(uri);

            // Unparseable input (rare for a stored webhook) — fall back to masking everything after
            // the first '/' so the host-ish prefix survives. With no '/' at all the value is one
            // opaque blob (e.g. a bare token), so nothing is safe to echo: fail closed. This path is
            // best-effort; the structured path above is the one real webhook URLs take.
            var firstSlash = url.IndexOf('/', StringComparison.Ordinal);
            return firstSlash < 0 ? MaskMarker : url.Substring(0, firstSlash + 1) + MaskMarker;
        }

        // True iff url carries the mask marker — i.e. it's a value we (or someone typing the literal
        // bullets) emitted, not a real webhook URL. Real URLs never contain `••••`.
        public static bool IsMasked(string url) => url != null && url.Contains(MaskMarker);

        // Classifies a posted webhook value against the stored URL. Returns null when the post is
        // acceptable (empty = no change; unchanged sentinel; or a real URL), and an error message
        // when it must be rejected. The "unchanged sentinel" check compares against Mask(stored) —
        // NOT just IsMasked — because the field is a plain editable input pre-filled with the mask,
        // so an admin who partially edits it (e.g. changes only the host) would otherwise have the
        // edit silently dropped by ResolveWebhook (IsMasked is a substring test, see #1329 review).
        //
        //   null/empty posted                     → null  (no change; new chat stays empty)
        //   posted == Mask(stored)                → null  (the unchanged sentinel round-tripped)
        //   posted IsMasked but ≠ Mask(stored)    → error (partial edit of the masked value)
        //   otherwise                             → null  (a real pasted URL; caller runs Uri checks)
        public static string ValidatePosted(string posted, string storedUrl)
        {
            if (string.IsNullOrWhiteSpace(posted))
                return null;

            if (IsMasked(posted) && posted != Mask(storedUrl))
                return "Paste the full webhook URL to replace it, or leave the field unchanged.";

            return null;
        }

        // Rebuild the display value from the URI's structured parts: authority verbatim + every path
        // segment except the last non-empty one verbatim + the last non-empty segment masked. Query
        // and fragment are dropped from the display value (they're not needed to recognize the
        // webhook and a `?redirect=/x` query must not move the mask split past the token).
        private static string MaskAbsoluteUri(Uri uri)
        {
            // Build the authority from parsed parts instead of GetLeftPart(UriPartial.Authority) —
            // that includes userinfo, so a `https://user:pass@host/…` webhook would render the
            // credentials in cleartext. Uri.Authority excludes userinfo.
            var authority = uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            var segments = uri.Segments;

            // uri.Segments is `/`-prefixed and never empty for an absolute URI (worst case: ["/"]).
            // Walk back from the end to find the last segment that carries a non-slash token.
            var lastIdx = -1;
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(segments[i]) && segments[i] != "/")
                {
                    lastIdx = i;
                    break;
                }
            }

            if (lastIdx < 0)
                return authority + "/" + MaskMarker;

            // Leading segments verbatim (each already carries its trailing '/'); then the masked
            // last segment WITHOUT its trailing slash (slashes after the token were display noise).
            var prefix = new System.Text.StringBuilder();
            for (var i = 0; i < lastIdx; i++)
                prefix.Append(segments[i]);
            var lastSegment = segments[lastIdx].TrimEnd('/');
            return authority + prefix.ToString() + MaskLastSegment(lastSegment);
        }

        // Reduces the last path segment to `head4 + MaskMarker + tail4`. A short segment
        // (≤ head+tail chars) is masked entirely: revealing anything of a short token leaves too
        // little hidden, and a mask must never emit the secret verbatim (#1329 review).
        private static string MaskLastSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return MaskMarker;

            if (segment.Length <= LastSegmentRevealThreshold)
                return MaskMarker;

            var head = segment.Substring(0, LastSegmentHeadChars);
            var tail = segment.Substring(segment.Length - LastSegmentTailChars);
            return head + MaskMarker + tail;
        }
    }
}
