namespace HSMServer.Validation
{
    /// <summary>
    /// #1705: the characters no legitimate icon, message template or name
    /// ever carries — C0 control characters (NUL above all; tab, newline and
    /// carriage return stay allowed in a message template, which may be
    /// multi-line — a Windows CRLF paste and a pre-change stored \r\n
    /// round-trip), DEL, the C1 controls, and the bidirectional
    /// embeddings/overrides/isolates that spoof display order (a name ending
    /// in U+202E reads backwards). Raw HTML is deliberately NOT rejected
    /// here: the renderers escape or sanitize it (the #1705 audit), and an
    /// emoji-bearing icon is a legitimate value.
    /// Single-line surfaces (names, icons) pass <c>allowLineBreaks: false</c>
    /// — a newline in a name breaks single-line rendering in lists,
    /// dropdowns and Telegram headers (a smaller cousin of the display
    /// spoofing this validator exists to stop). There the Unicode line
    /// separators U+2028/U+2029 are rejected with it: they are not
    /// control characters, but browsers and Telegram break lines on them
    /// all the same. The soft direction marks (U+200E/U+200F/U+061C) are
    /// deliberately still allowed everywhere: they are ordinary marks of
    /// mixed-direction text, outside #1705's embedding/override/isolate
    /// set.
    /// </summary>
    internal static class TextFieldValidator
    {
        public static bool HasForbiddenCharacter(string text, out string found, bool allowLineBreaks = true)
        {
            found = null;

            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var c in text)
            {
                if (IsAllowed(c, allowLineBreaks))
                    continue;

                found = $"U+{(int)c:X4}";

                return true;
            }

            return false;
        }

        private static bool IsAllowed(char c, bool allowLineBreaks) =>
            (allowLineBreaks && c is '\t' or '\n' or '\r') ||
            (c >= ' ' && c < '\u007F') ||
            (c > '\u009F' &&
             c is not (>= '\u202A' and <= '\u202E') and not (>= '\u2066' and <= '\u2069') &&
             (allowLineBreaks || c is not ('\u2028' or '\u2029')));
    }
}
