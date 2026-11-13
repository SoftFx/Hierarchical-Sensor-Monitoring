using System;

namespace HSMServer.Core.Services
{
    /// <summary>
    /// #1709: string sensor values have carried full connection strings with
    /// embedded credentials (mongodb://user:pass@host), readable by every
    /// role with read access. Mask the credential part at INGESTION: the
    /// scheme and host stay readable for monitoring, the secret never
    /// reaches storage. Already-stored values keep their secret — rotate the
    /// exposed credential (the issue tracks the ops action).
    /// </summary>
    public static class CredentialMasker
    {
        public const string Mask = "://***@";

        private const string MaskTail = "***@";

        public static string MaskCredentials(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var result = value;
            var searchFrom = 0;

            while (searchFrom < result.Length)
            {
                var schemeAt = result.IndexOf("://", searchFrom, StringComparison.Ordinal);

                if (schemeAt < 0)
                    break;

                var authorityAt = schemeAt + 3;
                var wordEnd = WordEnd(result, authorityAt);
                var separator = SeparatorOf(result, authorityAt, wordEnd);

                if (separator < 0)
                {
                    searchFrom = authorityAt;
                    continue;
                }

                result = string.Concat(result.AsSpan(0, authorityAt), MaskTail, result.AsSpan(separator + 1));
                searchFrom = authorityAt + MaskTail.Length;
            }

            return result;
        }

        private static int WordEnd(string value, int from)
        {
            var end = from;

            while (end < value.Length && !char.IsWhiteSpace(value[end]))
                end++;

            return end;
        }

        // The index of the credential-separating '@' within [from, wordEnd),
        // or -1 when the word carries no credential. Producers that build
        // connection strings by plain concatenation do not percent-encode the
        // password, so '@', '/', '?' and '#' may appear raw in it (#1709
        // review); the discriminator against a plain host:port URL (whose
        // colon tail is digits) is the port rule in LooksLikeHostPort.
        private static int SeparatorOf(string value, int from, int wordEnd)
        {
            var authorityEnd = wordEnd;

            for (var i = from; i < wordEnd; i++)
            {
                if (value[i] is '/' or '?' or '#')
                {
                    authorityEnd = i;
                    break;
                }
            }

            // 1) The common shape: the authority (up to the first path
            //    delimiter) contains '@' — the LAST one separates the
            //    credential (a raw '@' in mongodb://u:p@ss@host belongs to
            //    the password, not the host).
            var separator = LastIndexInRange(value, '@', from, authorityEnd);

            // 2) No '@' in the authority. When the authority cannot be
            //    host[:port] (mongodb://backup:pa/ss@host/db — the colon tail
            //    is not digits), the password runs past the delimiter and the
            //    separator is the last '@' of the whole word. A port-shaped
            //    authority (http://host:8080/path?email=a@b) never takes this
            //    branch, so an '@' in the path or query stays untouched.
            if (separator < 0 && !LooksLikeHostPort(value, from, authorityEnd))
                separator = LastIndexInRange(value, '@', from, wordEnd);

            if (separator < 0)
                return -1;

            // The credential must carry the password marker ':' with a
            // non-empty tail (redis://:pass@host has an empty user); a bare
            // scheme://user@host is not a secret.
            var userinfoColon = IndexInRange(value, ':', from, separator);

            if (userinfoColon < 0 || userinfoColon == separator - 1)
                return -1;

            // The host segment must be host-shaped: no '@' inside, and a
            // port, if present, is the digits-only tail.
            var hostEnd = wordEnd;

            for (var i = separator + 1; i < wordEnd; i++)
            {
                if (value[i] is '/' or '?' or '#')
                {
                    hostEnd = i;
                    break;
                }
            }

            return LooksLikeHostPort(value, separator + 1, hostEnd) &&
                   !ContainsInRange(value, '@', separator + 1, hostEnd)
                ? separator
                : -1;
        }

        // host, or host:port with an all-digit port and a non-empty host.
        // Anything else (a colon tail that is not digits, several colons,
        // an empty segment) cannot be an authority, so it reads as the
        // user:password shape instead.
        private static bool LooksLikeHostPort(string value, int from, int end)
        {
            if (from >= end)
                return false;

            var colon = -1;

            for (var i = from; i < end; i++)
            {
                if (value[i] != ':')
                    continue;

                if (colon >= 0)
                    return false; // host:port carries exactly one colon

                colon = i;
            }

            if (colon < 0)
                return true;

            if (colon == from || colon == end - 1)
                return false;

            for (var i = colon + 1; i < end; i++)
                if (!char.IsDigit(value[i]))
                    return false;

            return true;
        }

        private static int IndexInRange(string value, char of, int from, int end)
        {
            for (var i = from; i < end; i++)
                if (value[i] == of)
                    return i;

            return -1;
        }

        private static int LastIndexInRange(string value, char of, int from, int end)
        {
            for (var i = end - 1; i >= from; i--)
                if (value[i] == of)
                    return i;

            return -1;
        }

        private static bool ContainsInRange(string value, char of, int from, int end) =>
            IndexInRange(value, of, from, end) >= 0;
    }
}
