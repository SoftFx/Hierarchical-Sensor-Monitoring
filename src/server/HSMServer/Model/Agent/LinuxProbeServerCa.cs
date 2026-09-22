using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Model.Agent
{
    public enum LinuxProbeCaDecision
    {
        /// <summary>No server-ca.pem: a proxy terminates TLS, or there is nothing to export.</summary>
        Omit,

        /// <summary>Ship the server's own certificate as server-ca.pem.</summary>
        Include,

        /// <summary>
        /// Refuse the download: the server presents the bundled default certificate, whose private key
        /// is in the repository. Trusting it would let anyone impersonate this server to the probe host.
        /// </summary>
        RefuseBundledDefault,
    }


    /// <summary>
    /// Decides whether the Linux probe bundle carries <c>server-ca.pem</c> and produces its content: the
    /// PUBLIC certificate Kestrel presents, never a private key (#1424, §4.6). install.sh adds it to the
    /// system trust store so the probe keeps peer and hostname verification on; the Linux probe has no
    /// allow-untrusted switch.
    /// </summary>
    public static class LinuxProbeServerCa
    {
        public const string BundledDefaultMessage =
            "This server presents the bundled default TLS certificate. Its private key is public (it ships with HSM), " +
            "so a Linux probe must not be told to trust it. Configure a server certificate (Configuration > Server) " +
            "or run HSM behind a TLS-terminating proxy, then download the Linux probe again.";


        /// <summary>
        /// Pure decision. Behind a TLS-terminating proxy (plain-HTTP mode, Caddy + Let's Encrypt) the
        /// certificate clients see is the proxy's and is already publicly trusted: nothing ships. When the
        /// server terminates TLS itself, its certificate ships unless it is the bundled default (refused)
        /// or cannot be read (omitted; the download still works).
        /// </summary>
        public static LinuxProbeCaDecision Decide(bool serverTerminatesTls, bool isBundledDefault, bool hasCertificate)
        {
            if (!serverTerminatesTls)
                return LinuxProbeCaDecision.Omit;

            if (isBundledDefault)
                return LinuxProbeCaDecision.RefuseBundledDefault;

            return hasCertificate ? LinuxProbeCaDecision.Include : LinuxProbeCaDecision.Omit;
        }

        /// <summary>
        /// PEM of the certificate Kestrel serves, public part only. Only the leaf: an issuing CA a .pfx may
        /// also carry would become a trust anchor for every host the probe machine talks to, while the leaf
        /// alone vouches only for this server (OpenSSL accepts it as a partial-chain anchor).
        /// Null when the file cannot be read.
        /// </summary>
        public static string ExportLeafPem(string path, string password)
        {
            try
            {
                using var certificate = new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet);

                return certificate.ExportCertificatePem() + "\n";
            }
            catch (Exception ex) when (ex is CryptographicException or System.IO.IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return null;
            }
        }

        /// <summary>
        /// The decision plus the server-ca.pem content (null unless <see cref="LinuxProbeCaDecision.Include"/>).
        /// <paramref name="certificateSource"/> is only evaluated when the server terminates TLS.
        /// </summary>
        public static (LinuxProbeCaDecision Decision, string Pem) Resolve(bool serverTerminatesTls, Func<(string Path, string Password, bool IsBundledDefault)> certificateSource)
        {
            if (!serverTerminatesTls)
                return (LinuxProbeCaDecision.Omit, null);

            var (path, password, isBundledDefault) = certificateSource();
            var pem = isBundledDefault ? null : ExportLeafPem(path, password);
            var decision = Decide(serverTerminatesTls, isBundledDefault, pem is not null);

            return (decision, decision == LinuxProbeCaDecision.Include ? pem : null);
        }
    }
}
