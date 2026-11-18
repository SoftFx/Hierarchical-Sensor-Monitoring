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
    /// mask (#1709 review). The authority ends at a HEURISTIC boundary set
    /// — the characters RFC 3986 excludes from an authority, plus the
    /// sub-delims ' ( ) , ; that commonly delimit JSON and prose — so a
    /// URL without a path cannot "own" the rest of its word: a later JSON
    /// field or an email address after a comma stays intact instead of
    /// reading as the credential tail. The
    /// scan is word-by-word and linear in the input length — every character
    /// is examined a bounded number of times, so an adversarial megabyte
    /// value cannot pin a request thread.
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

                // The authority ends at the first '/', '?', '#' or character
                // RFC 3986 excludes from it entirely — except the ';' or '#'
                // closing an HTML entity, which is encoded TEXT, not a
                // boundary; the shared pointer never rescans what an earlier
                // "://" already covered.
                if (authorityEnd < authorityAt)
                    authorityEnd = authorityAt;

                while (authorityEnd < wordEnd)
                {
                    var boundary = value[authorityEnd];

                    if (!IsAuthorityEnd(boundary))
                    {
                        authorityEnd++;
                        continue;
                    }

                    // In the sanitizer's output '&' always travels as an
                    // entity, and a password may legally contain it —
                    // mongodb://u:p&amp;ss@host (#1709 review).
                    if ((boundary == ';' || boundary == '#') && EndsEntity(value, authorityEnd))
                        authorityEnd++;
                    else
                        break;
                }

                // 1) The common shape: the authority contains '@' — the LAST
                //    one separates the credential (a raw '@' in
                //    mongodb://u:p@ss@host belongs to the password, not the host).
                var separator = LastIndexInRange(value, '@', authorityAt, authorityEnd);

                // 2) No '@' in the authority. When the authority cannot be
                //    host[:port] (mongodb://backup:pa/ss@host/db — a raw '/',
                //    '?' or '#' in the password ends the authority scan early),
                //    the password runs past the delimiter and the separator is
                //    the last '@' of the credential zone. The zone crosses
                //    '/', '?' and '#' (that is its point) and a raw '<' — the
                //    sanitizer drops everything from an unclosed '<tag' shape,
                //    so a '<' inside the password must mask BEFORE the
                //    sanitizer runs (#1709 review) — but no other boundary
                //    character: an email in the next JSON field is not a
                //    password tail (#1709 review).
                //    When the zone finds no '@' at all: a zone that stopped
                //    at a boundary proves its whole span '@'-free, so every
                //    "://" inside it cannot mask either and the scan resumes
                //    FROM the boundary (a later well-formed URL in the same
                //    word still masks); only a zone that reached the END of
                //    the word proves nothing ahead can mask — that case (and
                //    only it) breaks, which keeps the scan linear: without
                //    the break a word whose only '@' sits BEFORE the first
                //    "://" (@a://q:x/@a://q:x/...) makes every hit rescan
                //    the whole tail — quadratic on the request thread.
                if (separator < 0 && PasswordMayRunPastDelimiters(value, authorityAt, authorityEnd))
                {
                    var credentialEnd = authorityAt;

                    while (credentialEnd < wordEnd)
                    {
                        var boundary = value[credentialEnd];

                        if (!IsCredentialZoneEnd(boundary))
                        {
                            credentialEnd++;
                            continue;
                        }

                        if ((boundary == ';' || boundary == '#') && EndsEntity(value, credentialEnd))
                            credentialEnd++;
                        else
                            break;
                    }

                    separator = LastIndexInRange(value, '@', authorityAt, credentialEnd);

                    if (separator < 0)
                    {
                        if (credentialEnd == wordEnd)
                            break;

                        searchFrom = credentialEnd;
                        continue;
                    }
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
        // authority may take it: an EMPTY authority (a bracketed IPv6 host
        // ends the scan at its '[' immediately) and a host:port authority
        // are hosts, and their later '@'s live in the path
        // (http://[::1]/a:b@c, file:///C:/x@y) — masking there would
        // destroy credential-free values.
        private static bool PasswordMayRunPastDelimiters(string value, int from, int end) =>
            from < end && !LooksLikeHostPort(value, from, end);

        // host, or host:port with an all-digit port — possibly EMPTY: RFC
        // 3986 allows 'port = *DIGIT', so a TRAILING colon is still a
        // host:port (http://example.com:/a@b) and its later '@'s live in
        // the path (#1709 review). A leading colon or several colons
        // cannot be an authority, so they read as the user:password shape.
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

            if (colon == from)
                return false; // an empty host is not host:port

            // colon == end - 1: an EMPTY port is still host:port — RFC
            // 3986 defines 'port = *DIGIT', so the digit loop below is
            // empty and the value stays a host whose '@'s live in the
            // path (#1709 review).
            for (var i = colon + 1; i < end; i++)
                if (!char.IsDigit(value[i]))
                    return false;

            return true;
        }

        // The path delimiters, plus the heuristic boundary set: the
        // characters RFC 3986 excludes from an authority, PLUS the
        // sub-delims ' ( ) , ; — legal there, but they commonly delimit
        // JSON and prose. Without them a URL without a path "owns" the
        // rest of its whitespace-free word:
        // http://example.com","owner":"admin@corp.com would read its own
        // JSON tail as user:password@ and destroy it (#1709 review).
        private static bool IsAuthorityEnd(char ch) =>
            ch is '/' or '?' or '#'
                or '"' or '\'' or ',' or ';' or '<' or '>' or '(' or ')'
                or '{' or '}' or '[' or ']' or '\\' or '|';

        // The same set minus the path delimiters and minus '<': the
        // raw-password fallback exists to reach across '/', '?' and '#',
        // and it also crosses a raw '<' — the sanitizer drops everything
        // from an unclosed '<tag' shape, so a '<' inside a password must
        // mask before the sanitizer runs (#1709 review). A raw '>' starts
        // nothing and travels as an entity after the sanitizer, so it
        // still ends the zone; every other character above cannot sit
        // inside a credential, so it ends the zone too.
        private static bool IsCredentialZoneEnd(char ch) =>
            ch is '"' or '\'' or ',' or ';' or '>' or '(' or ')'
                or '{' or '}' or '[' or ']' or '\\' or '|';

        // Whether the ';' or '#' at 'at' closes an HTML entity: '&' followed
        // by 1-9 entity characters (letters, digits, '#'). In the sanitizer's
        // output '&' always travels entity-encoded (&amp;), and the entity is
        // the TEXT it encodes — a password may legally contain '&', so the
        // boundary scans step over the whole entity instead of splitting the
        // credential at its ';' (#1709 review).
        private static bool EndsEntity(string value, int at)
        {
            var start = at - 9;

            if (start < 0)
                start = 0;

            for (var i = at - 1; i >= start; i--)
            {
                var ch = value[i];

                if (ch == '&')
                    return true;

                if (!char.IsLetterOrDigit(ch) && ch != '#')
                    return false;
            }

            return false;
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
    }
}
