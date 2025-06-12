using System.Text.RegularExpressions;
using System.Text;

namespace HSMServer.Extensions
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
            if (message != null)
                for (int i = 0; i < _escapedSymbols.Length; i++)
                    message = message.Replace(_specialSymbolsMarkdownV2[i], _escapedSymbols[i]);

            return message;
        }

        public static string EscapeMarkdownV2ExceptPlaceholders(this string template)
        {
            if (template == null)
                return null;

            var sb = new StringBuilder(template.Length);
            var regex = new Regex(@"(\{[0-9]+\}|[^{]+|{)"); 

            foreach (Match match in regex.Matches(template))
            {
                string part = match.Value;

                if (Regex.IsMatch(part, @"^\{[0-9]+\}$"))
                {
                    sb.Append(part);
                }
                else 
                {
                    sb.Append(EscapeMarkdownV2(part));
                }
            }

            return sb.ToString();
        }

    }
}
