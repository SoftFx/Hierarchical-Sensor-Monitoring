using Ganss.Xss;

namespace HSMServer.Core.Services
{
    public static class HtmlSanitizerService
    {
        private static readonly HtmlSanitizer _sanitizer;

        static HtmlSanitizerService()
        {
            _sanitizer = new HtmlSanitizer();

            _sanitizer.AllowedTags.Clear();
            _sanitizer.AllowedTags.Add("markdown");
            _sanitizer.AllowedTags.Add("br");
            _sanitizer.AllowedAttributes.Clear();

            _sanitizer.AllowedSchemes.Clear();

            _sanitizer.AllowDataAttributes = false;
            _sanitizer.KeepChildNodes = true;
        }

        public static string Sanitize(string dirtyHtml)
        {
            if (dirtyHtml is null)
                return dirtyHtml;

            return _sanitizer.Sanitize(dirtyHtml);
        }

    }
}
