namespace HSMServer.Validation
{
    /// <summary>
    /// #1705: the characters no legitimate icon, message template or name
    /// ever carries — C0 control characters (NUL above all; tab and newline
    /// stay allowed, a message template may be multi-line), DEL, the C1
    /// controls, and the bidirectional embeddings/overrides/isolates that
    /// spoof display order (a name ending in U+202E reads backwards).
    /// Raw HTML is deliberately NOT rejected here: the renderers escape or
    /// sanitize it (the #1705 audit), and an emoji-bearing icon is a
    /// legitimate value.
    /// </summary>
    internal static class TextFieldValidator
    {
        public static bool HasForbiddenCharacter(string text, out string found)
        {
            found = null;

            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var c in text)
            {
                if (IsAllowed(c))
                    continue;

                found = $"U+{(int)c:X4}";

                return true;
            }

            return false;
        }

        private static bool IsAllowed(char c) =>
            c is '\t' or '\n' ||
            (c >= ' ' && c < '\u007F') ||
            (c > '\u009F' && c is not (>= '\u202A' and <= '\u202E') and not (>= '\u2066' and <= '\u2069'));
    }
}
