using Ganss.Xss;

namespace HSMServer.Services
{
    public interface IHtmlSanitizerService
    {
        string Sanitize(string dirtyHtml);
    }

    public class HtmlSanitizerService : IHtmlSanitizerService
    {
        private readonly HtmlSanitizer _sanitizer;

        public HtmlSanitizerService()
        {
            _sanitizer = new HtmlSanitizer();

            _sanitizer.AllowedTags.Clear();
            _sanitizer.AllowedTags.Add("markdown");

            _sanitizer.AllowedAttributes.Clear();

            _sanitizer.AllowedSchemes.Clear();

            _sanitizer.AllowDataAttributes = false;
            _sanitizer.KeepChildNodes = true;
        }

        public string Sanitize(string dirtyHtml)
        {
            if (dirtyHtml is null)
                return dirtyHtml;

            return _sanitizer.Sanitize(dirtyHtml);
        }

    }
}
