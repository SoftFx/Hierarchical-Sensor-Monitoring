namespace HSMServer.Core.Extensions
{
    public static class DisplayTextExtensions
    {
        /// <summary>What a bidirectional control character is shown as: the replacement character, visible.</summary>
        public const char Neutralized = (char)0xFFFD;


        /// <summary>
        /// A client's text (a file name, a comment, a client name, an address) as it is shown on a page or written to an
        /// export: the bidirectional control characters (the embeddings, overrides and isolates and the direction marks)
        /// are replaced by <see cref="Neutralized"/>, so the text reads in the order it was written.
        /// </summary>
        public static string WithoutBidiControls(this string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(BidiControls) < 0)
                return text;

            var chars = text.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
                if (IsBidiControl(chars[i]))
                    chars[i] = Neutralized;

            return new string(chars);
        }


        private static readonly char[] BidiControls =
        [
            (char)0x061C, (char)0x200E, (char)0x200F,
            (char)0x202A, (char)0x202B, (char)0x202C, (char)0x202D, (char)0x202E,
            (char)0x2066, (char)0x2067, (char)0x2068, (char)0x2069,
        ];

        private static bool IsBidiControl(char ch) => System.Array.IndexOf(BidiControls, ch) >= 0;
    }
}
