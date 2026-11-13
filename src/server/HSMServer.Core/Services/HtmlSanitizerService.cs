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

        /// <summary>
        /// The single ingestion gate for user-supplied text (#1709 review):
        /// the credential mask first (so a masked URL never depends on the
        /// sanitizer's transformations), then the HTML sanitizer. Every
        /// converter — both copies, value AND comment of every sensor type —
        /// runs its text through here, so the two cannot drift apart.
        /// </summary>
        public static string SanitizeText(string text) =>
            Sanitize(CredentialMasker.MaskCredentials(text));

    }
}
