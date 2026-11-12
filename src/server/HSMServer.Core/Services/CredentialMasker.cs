using System.Text.RegularExpressions;

namespace HSMServer.Core.Services
{
    /// <summary>
    /// #1709: string sensor values have carried full connection strings with
    /// embedded credentials (mongodb://user:pass@host), readable by every
    /// role with read access. Mask the credential part at INGESTION: the
    /// scheme and host stay readable for monitoring, the secret never
    /// reaches storage. Already-stored values keep their secret — rotate the
    /// exposed credential (the issue tracks the ops action).
    /// </summary>
    public static class CredentialMasker
    {
        // scheme://user:pass@ — the user may be empty (redis://:pass@host);
        // the password ends at the first '@' (userinfo cannot contain an
        // unencoded '@') and carries no '/', '?' or '#', so a plain
        // host:port URL (http://host:8080/path?email=a@b) never matches — a
        // secret containing those characters travels percent-encoded in a
        // valid URL. A bare user with no password (scheme://user@host) is
        // not a secret and stays as-is.
        private static readonly Regex CredentialUrl = new(@"://[^:/@\s]*:[^@\s/?#]+@", RegexOptions.Compiled);

        public const string Mask = "://***@";

        public static string MaskCredentials(string value) =>
            value is null ? value : CredentialUrl.Replace(value, Mask);
    }
}
