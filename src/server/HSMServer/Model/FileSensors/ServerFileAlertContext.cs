using System;
using HSMServer.Core.Model.Policies;
using HSMServer.ServerConfiguration;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// The server's <see cref="IFileAlertContext"/> (#1886). The base of <c>$link</c>: <c>HSM_DOMAIN</c> (the address the
    /// bundled Caddy serves the web UI on), else <c>Agent.ExternalConnectionUrl</c>, else none: the link is then the path
    /// of the page alone, which a chat does not make clickable.
    /// </summary>
    public sealed class ServerFileAlertContext : IFileAlertContext
    {
        public const string DomainVariable = "HSM_DOMAIN";

        private readonly IServerConfig _config;
        private readonly Func<string> _domain;
        private readonly Func<Guid, Guid, bool> _waitsForDumpPassword;


        /// <param name="waitsForDumpPassword">(product, file): a secure large file of a product without a dump password.</param>
        public ServerFileAlertContext(IServerConfig config, Func<Guid, Guid, bool> waitsForDumpPassword)
            : this(config, waitsForDumpPassword, () => Environment.GetEnvironmentVariable(DomainVariable)) { }

        internal ServerFileAlertContext(IServerConfig config, Func<Guid, Guid, bool> waitsForDumpPassword, Func<string> domain)
        {
            _config = config;
            _waitsForDumpPassword = waitsForDumpPassword;
            _domain = domain;
        }


        public string SensorPageLink(Guid sensorId) => $"{BaseAddress()}{FileAlertContext.SensorPagePath(sensorId)}";

        public bool WaitsForDumpPassword(Guid productId, Guid fileId) => _waitsForDumpPassword(productId, fileId);


        /// <summary>Scheme, host and an explicit port, without a trailing slash; empty when none is known.</summary>
        internal string BaseAddress() => FromDomain(_domain()) ?? FromUrl(_config?.Agent?.ExternalConnectionUrl) ?? string.Empty;


        private static string FromDomain(string domain)
        {
            domain = domain?.Trim();

            if (string.IsNullOrEmpty(domain))
                return null;

            // A bare IPv6 address needs its brackets in a URL.
            if (domain.Contains(':') && !domain.StartsWith('['))
                domain = $"[{domain}]";

            return FromUrl($"https://{domain}");
        }

        private static string FromUrl(string url)
        {
            url = url?.Trim();

            if (string.IsNullOrEmpty(url))
                return null;

            if (!url.Contains("://"))
                url = $"https://{url}";

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return null;

            return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }
    }
}
