using Markdig;
using Microsoft.AspNetCore.Html;
using Ganss.Xss;

namespace HSMServer.Helpers
{
    public static class MarkdownHelper
    {
        private static readonly HtmlSanitizer _sanitizer = new();

        public static IHtmlContent ToHtml(string text, bool removeParagraph = false)
        {
            if (string.IsNullOrEmpty(text))
                return HtmlString.Empty;

            var html = Markdown.ToHtml(text);

            var safeHtml = _sanitizer.Sanitize(html);

            if (removeParagraph)
                safeHtml = safeHtml.Replace("<p>", "<span>")
                                   .Replace("</p>", "</span>");

            return new HtmlString(safeHtml);
        }
    }
}
