using System;
using System.Text;

namespace HSMServer.Core.Services
{
    /// <summary>
    /// #1709: string sensor values have carried full connection strings with
    /// embedded credentials (mongodb://user:pass@host), readable by every
    /// role with read access. Mask the credential part at INGESTION: the
    /// scheme and the host stay readable for monitoring, the secret never
    /// reaches storage. Already-stored values keep their secret — rotate the
    /// exposed credential (the issue tracks the ops action).
    /// A credential is user:password@ INSIDE the authority; the host after
    /// it is never parsed, so replica-set host lists, bracketed IPv6 hosts
    /// and URLs embedded in JSON, parentheses or trailing punctuation still
    /// mask (#1709 review). The scan is word-by-word and linear in the input
    /// length — every character is examined a bounded number of times, so an
    /// adversarial megabyte value cannot pin a request thread.
    /// </summary>
    public static class CredentialMasker
    {
        private const string SchemeSeparator = "://";

        private const string MaskTail = "***@";

        public static string MaskCredentials(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Masking needs both markers; most values and comments carry
            // neither — two scans, no allocation.
            if (value.IndexOf(SchemeSeparator, StringComparison.Ordinal) < 0 || value.IndexOf('@') < 0)
                return value;

            var builder = new StringBuilder(value.Length);
            var masked = false;
            var position = 0;

            while (position < value.Length)
            {
                if (char.IsWhiteSpace(value[position]))
                {
                    builder.Append(value[position]);
                    position++;
                    continue;
                }

                var wordEnd = position;

                while (wordEnd < value.Length && !char.IsWhiteSpace(value[wordEnd]))
                    wordEnd++;

                masked |= AppendMaskedWord(value, position, wordEnd, builder);
                position = wordEnd;
            }

            return masked ? builder.ToString() : value;
        }

        // Appends the word [wordStart, wordEnd) with every scheme://user:password@
        // inside it masked; tells whether anything was masked. The word is read
        // through its ORIGINAL indices — masking only changes what is appended,
        // never the scanned input — and words without both markers copy through
        // untouched (an '@'-free battery of "://", the cheapest adversarial
        // shape, costs one scan and no loop).
        private static bool AppendMaskedWord(string value, int wordStart, int wordEnd, StringBuilder builder)
        {
            if (value.IndexOf(SchemeSeparator, wordStart, wordEnd - wordStart, StringComparison.Ordinal) < 0 ||
                IndexInRange(value, '@', wordStart, wordEnd) < 0)
            {
                builder.Append(value, wordStart, wordEnd - wordStart);
                return false;
            }

            var masked = false;
            var copyFrom = wordStart;     // start of the pending verbatim run
            var searchFrom = wordStart;   // where the next "://" search starts
            var authorityEnd = wordStart; // shared pointer: first path delimiter of the current authority

            while (searchFrom < wordEnd)
            {
                var schemeAt = value.IndexOf(SchemeSeparator, searchFrom, wordEnd - searchFrom, StringComparison.Ordinal);

                if (schemeAt < 0)
                    break;

                var authorityAt = schemeAt + SchemeSeparator.Length;

                // The authority ends at the first '/', '?' or '#'; the shared
                // pointer never rescans what an earlier "://" already covered.
                if (authorityEnd < authorityAt)
                    authorityEnd = authorityAt;

                while (authorityEnd < wordEnd && !IsPathDelimiter(value[authorityEnd]))
                    authorityEnd++;

                // 1) The common shape: the authority contains '@' — the LAST
                //    one separates the credential (a raw '@' in
                //    mongodb://u:p@ss@host belongs to the password, not the host).
                var separator = LastIndexInRange(value, '@', authorityAt, authorityEnd);

                // 2) No '@' in the authority. When the authority cannot be
                //    host[:port] (mongodb://backup:pa/ss@host/db — a raw '/',
                //    '?' or '#' in the password ends the authority scan early),
                //    the password runs past the delimiter and the separator is
                //    the last '@' of the whole word. That scan reaches the END
                //    of the word: when it too finds no '@', no later "://"
                //    searches far enough right to see one, so the word is done.
                //    Without the break, a word whose only '@' sits BEFORE the
                //    first "://" (@a://q:x/@a://q:x/...) makes every hit
                //    rescan the whole tail — quadratic on the request thread.
                if (separator < 0 && PasswordMayRunPastDelimiters(value, authorityAt, authorityEnd))
                {
                    separator = LastIndexInRange(value, '@', authorityAt, wordEnd);

                    if (separator < 0)
                        break;
                }

                // The credential must carry the password marker ':' with a
                // non-empty tail: redis://:pass@host has an EMPTY user (mask),
                // while scheme://user:@host and scheme://user@host carry no
                // password — nothing to leak, leave them.
                var passwordColon = separator >= 0
                    ? IndexInRange(value, ':', authorityAt, separator)
                    : -1;

                if (passwordColon < 0 || passwordColon == separator - 1)
                {
                    searchFrom = schemeAt + 1;
                    continue;
                }

                builder.Append(value, copyFrom, authorityAt - copyFrom);
                builder.Append(MaskTail);
                copyFrom = separator + 1;
                masked = true;
                searchFrom = separator + 1;
            }

            builder.Append(value, copyFrom, wordEnd - copyFrom);

            return masked;
        }

        // The fallback of the raw-password case — only a userinfo-shaped
        // authority may take it: an EMPTY authority, a bracketed IPv6 host
        // ([::1]) and a host:port authority are hosts, and their later '@'s
        // live in the path (http://[::1]/a:b@c, file:///C:/x@y) — masking
        // there would destroy credential-free values.
        private static bool PasswordMayRunPastDelimiters(string value, int from, int end)
        {
            if (from >= end || value[from] == '[')
                return false;

            return !LooksLikeHostPort(value, from, end);
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

        private static bool IsPathDelimiter(char ch) =>
            ch is '/' or '?' or '#';

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
    }
}
