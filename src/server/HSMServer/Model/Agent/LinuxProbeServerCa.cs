using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Model.Agent
{
    public enum LinuxProbeCaDecision
    {
        /// <summary>No server-ca.pem: a proxy terminates TLS, so the certificate clients see is the proxy's.</summary>
        Omit,

        /// <summary>
        /// No server-ca.pem because the server's own certificate could not be read. The download still
        /// works, but the caller must say so: a probe only connects if that certificate is already trusted.
        /// </summary>
        OmitUnreadable,

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
    /// PUBLIC part of the certificate Kestrel presents, never a private key (#1424, §4.6). install.sh adds
    /// it to the system trust store so the probe keeps peer and hostname verification on; the Linux probe
    /// has no allow-untrusted switch.
    /// </summary>
    public static class LinuxProbeServerCa
    {
        public const string BundledDefaultMessage =
            "This server presents the bundled default TLS certificate. Its private key is public (it ships with HSM), " +
            "so a Linux probe must not be told to trust it. Configure a server certificate (Configuration > Server) " +
            "and restart, or run HSM behind a TLS-terminating proxy, then download the Linux probe again.";


        /// <summary>
        /// Pure decision. Behind a TLS-terminating proxy (plain-HTTP mode, Caddy + Let's Encrypt) the
        /// certificate clients see is the proxy's and is already publicly trusted: nothing ships. When the
        /// server terminates TLS itself, its certificate ships unless it is the bundled default (refused)
        /// or cannot be read (omitted and reported; the download still works).
        /// </summary>
        public static LinuxProbeCaDecision Decide(bool serverTerminatesTls, bool isBundledDefault, bool hasCertificate)
        {
            if (!serverTerminatesTls)
                return LinuxProbeCaDecision.Omit;

            if (isBundledDefault)
                return LinuxProbeCaDecision.RefuseBundledDefault;

            return hasCertificate ? LinuxProbeCaDecision.Include : LinuxProbeCaDecision.OmitUnreadable;
        }

        /// <summary>
        /// PEM of the served certificate, public part only. Only the leaf: an issuing CA would become a
        /// trust anchor for every host the probe machine talks to, while the leaf vouches only for this
        /// server. libcurl (which the collector's transport uses with its defaults) sets OpenSSL's
        /// partial-chain flag, so a CA-issued leaf in the trust store verifies on its own; checked with
        /// curl 8.14 / OpenSSL 3.5 on Debian 13.
        /// </summary>
        public static string ExportPem(X509Certificate2 certificate) => certificate.ExportCertificatePem() + "\n";

        /// <summary>
        /// The decision plus the server-ca.pem content (null unless <see cref="LinuxProbeCaDecision.Include"/>).
        /// <paramref name="servedCertificate"/> returns the certificate Kestrel serves and whether it is the
        /// bundled default; it is only evaluated when the server terminates TLS.
        /// </summary>
        public static (LinuxProbeCaDecision Decision, string Pem) Resolve(bool serverTerminatesTls, Func<(X509Certificate2 Certificate, bool IsBundledDefault)> servedCertificate)
        {
            if (!serverTerminatesTls)
                return (LinuxProbeCaDecision.Omit, null);

            X509Certificate2 certificate;
            bool isBundledDefault;
            try
            {
                (certificate, isBundledDefault) = servedCertificate();
            }
            catch (Exception ex) when (ex is CryptographicException or System.IO.IOException or UnauthorizedAccessException)
            {
                return (LinuxProbeCaDecision.OmitUnreadable, null);
            }

            var decision = Decide(serverTerminatesTls, isBundledDefault, certificate is not null);

            return (decision, decision == LinuxProbeCaDecision.Include ? ExportPem(certificate) : null);
        }
    }
}
