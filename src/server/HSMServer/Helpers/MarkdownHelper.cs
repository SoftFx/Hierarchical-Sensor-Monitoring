using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace HSMServer.Helpers
{
    public static class MarkdownHelper
    {
        private static readonly char[] _specialChars = ['_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'];

        private static HtmlSanitizer _sanitizer;

        private static readonly Regex MarkdownRegex = new Regex(
            @"(?<element>
            \*\*(?<bold>.+?)\*\*        |
            __(?<bold2>.+?)__           |
            \*(?<italic>.+?)\*          |
            _(?<italic2>.+?)_           |
            `(?<code>.+?)`              |
            ~~(?<strike>.+?)~~          |
            \[(?<link>.+?)\]\(.+?\)|
            !\[(?<image>.+?)\]\(.+?\)|
            (?<escaped>\\.)
             )",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline
        );

        static MarkdownHelper()
        {
            _sanitizer = new HtmlSanitizer();
            _sanitizer.AllowedTags.Clear();
            _sanitizer.AllowedTags.Add("b");
            _sanitizer.AllowedTags.Add("strong");
            _sanitizer.AllowedTags.Add("i");
            _sanitizer.AllowedTags.Add("em");
            _sanitizer.AllowedTags.Add("a");

            _sanitizer.AllowedAttributes.Clear();
            _sanitizer.AllowedAttributes.Add("href");
            _sanitizer.AllowedAttributes.Add("target");

            _sanitizer.AllowedSchemes.Clear();
            _sanitizer.AllowedSchemes.Add("http");
            _sanitizer.AllowedSchemes.Add("https");
            _sanitizer.AllowedSchemes.Add("mailto");

            _sanitizer.AllowDataAttributes = false;
            _sanitizer.KeepChildNodes = true;

        }


        public static IHtmlContent ToHtml(string markdown)
        {
            if (markdown is null)
                return HtmlString.Empty;

            return new HtmlString(_sanitizer.Sanitize(Markdown.ToHtml(markdown)));
        }

        public static string EscapeMarkdownV2(string text)
        {
            var result = new StringBuilder(text.Length*2);
            foreach (char c in text)
            {
                if (Array.IndexOf(_specialChars, c) >= 0)
                    result.Append('\\');
                result.Append(c);
            }
            return result.ToString();
        }

        public static string ConvertToMarkdownV2(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new StringBuilder(input.Length * 2);
            int lastPos = 0;

            foreach (Match match in MarkdownRegex.Matches(input))
            {
                if (match.Index > lastPos)
                {
                    result.Append(EscapeMarkdownV2(input.Substring(lastPos, match.Index - lastPos)));
                }

                if (match.Groups["escaped"].Success)
                {
                    result.Append(EscapeMarkdownV2(match.Value));
                }
                else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
                {
                    string text = match.Groups["bold"].Success ?
                        match.Groups["bold"].Value : match.Groups["bold2"].Value;
                    result.Append($"*{EscapeMarkdownV2(text)}*");
                }
                else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
                {
                    string text = match.Groups["italic"].Success ?
                        match.Groups["italic"].Value : match.Groups["italic2"].Value;
                    result.Append($"_{EscapeMarkdownV2(text)}_");
                }
                else if (match.Groups["code"].Success)
                {
                    result.Append($"`{EscapeMarkdownV2(match.Groups["code"].Value)}`");
                }
                else if (match.Groups["strike"].Success)
                {
                    result.Append($"~{EscapeMarkdownV2(match.Groups["strike"].Value)}~");
                }
                else if (match.Groups["link"].Success)
                {
                    string linkText = match.Groups["link"].Value;
                    result.Append($"[{EscapeMarkdownV2(linkText)}]{match.Value.Substring(linkText.Length + 2)}");
                }
                else if (match.Groups["image"].Success)
                {
                    string altText = match.Groups["image"].Value;
                    result.Append($"![{EscapeMarkdownV2(altText)}]{match.Value.Substring(altText.Length + 2)}");
                }

                lastPos = match.Index + match.Length;
            }

            if (lastPos < input.Length)
            {
                result.Append(EscapeMarkdownV2(input.Substring(lastPos)));
            }

            return result.ToString();
        }


    }
}
