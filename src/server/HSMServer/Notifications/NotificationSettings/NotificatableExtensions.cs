namespace HSMServer.Notification.Settings
{
    internal static class NotificatableExtensions
    {
        private static readonly string[] _specialSymbolsMarkdownV2 = new[]
            {"_", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!", "*"};

        private static string[] _escapedSymbols;


        static NotificatableExtensions()
        {
            BuildEscapedSymbols();
        }

        private static void BuildEscapedSymbols()
        {
            _escapedSymbols = new string[_specialSymbolsMarkdownV2.Length];

            for (int i = 0; i < _escapedSymbols.Length; i++)
                _escapedSymbols[i] = $"\\{_specialSymbolsMarkdownV2[i]}";
        }

        public static string EscapeMarkdownV2(this string message)
        {
            for (int i = 0; i < _escapedSymbols.Length; i++)
                message = message.Replace(_specialSymbolsMarkdownV2[i], _escapedSymbols[i]);

            return message;
        }
    }
}
