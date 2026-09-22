using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// Decides whether the Linux probe bundle carries <c>server-ca.pem</c> and produces its content: the
    /// PUBLIC certificate chain the server's own TLS listener presents, never a private key (#1424, §4.6).
    /// With the chain in the system trust store the probe keeps peer and hostname verification on; the
    /// Linux probe has no allow-untrusted switch.
    /// </summary>
    public static class LinuxProbeServerCa
    {
        /// <summary>
        /// Pure decision. Include the chain only when this server terminates TLS itself (its own,
        /// typically self-signed, certificate) and there is a certificate to export. Behind a
        /// TLS-terminating proxy (plain-HTTP mode, Caddy + Let's Encrypt) the certificate clients see
        /// is the proxy's and is already publicly trusted, so nothing is shipped.
        /// </summary>
        public static bool ShouldInclude(bool serverTerminatesTls, bool hasCertificate) => serverTerminatesTls && hasCertificate;

        /// <summary>PEM of the public certificates only, leaf first, duplicates dropped.</summary>
        public static string ExportPublicPem(IEnumerable<X509Certificate2> chain)
        {
            var pem = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var certificate in chain.Where(c => c is not null))
                if (seen.Add(certificate.Thumbprint))
                    pem.Append(certificate.ExportCertificatePem()).Append('\n');

            return pem.Length == 0 ? null : pem.ToString();
        }

        /// <summary>
        /// Reads the certificates of the file Kestrel loads its certificate from (a .pfx may carry
        /// the issuing CA next to the leaf), with the leaf first. Returns an empty list when the file
        /// cannot be read: the bundle then goes out without a CA file rather than failing the download.
        /// </summary>
        public static IReadOnlyList<X509Certificate2> LoadChain(string path, string password)
        {
            try
            {
                var collection = new X509Certificate2Collection();
                collection.Import(path, password, X509KeyStorageFlags.EphemeralKeySet);

                return collection.OrderByDescending(c => c.HasPrivateKey).ToList();
            }
            catch (Exception ex) when (ex is CryptographicException or System.IO.IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return Array.Empty<X509Certificate2>();
            }
        }

        /// <summary>The server-ca.pem content for this server, or null when the bundle must not carry one.</summary>
        /// <remarks><paramref name="certificateSource"/> is only evaluated when the server terminates TLS.</remarks>
        public static string Resolve(bool serverTerminatesTls, Func<(string Path, string Password)> certificateSource)
        {
            if (!serverTerminatesTls)
                return null;

            var (path, password) = certificateSource();
            var chain = LoadChain(path, password);
            try
            {
                return ShouldInclude(serverTerminatesTls, chain.Count > 0) ? ExportPublicPem(chain) : null;
            }
            finally
            {
                foreach (var certificate in chain)
                    certificate.Dispose();
            }
        }
    }
}
