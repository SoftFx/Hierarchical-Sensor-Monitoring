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
        /// mask, sanitize, mask again. The FIRST mask runs on the raw text
        /// and catches what the sanitizer would DESTROY: a raw '&lt;' in a
        /// password reads as an unclosed tag, and the sanitizer drops
        /// everything after it, so that shape must mask before it runs.
        /// The sanitizer then decodes entities and strips tags, and the
        /// SECOND mask catches what that rewrites into shape — an
        /// entity-encoded '@' (mongodb://u:p&amp;#64;h) or a tag splitting
        /// the URL from its '@'. The sanitizer re-encodes markup characters
        /// but leaves ':', '/' and '@' literal, so masking after it cannot
        /// reintroduce markup, and re-masking masked text is a no-op
        /// ('***@' carries no ':'). Every converter — both copies, value
        /// AND comment of every sensor type — runs its text through here,
        /// so the two cannot drift apart.
        /// </summary>
        public static string SanitizeText(string text) =>
            CredentialMasker.MaskCredentials(Sanitize(CredentialMasker.MaskCredentials(text)));

    }
}
