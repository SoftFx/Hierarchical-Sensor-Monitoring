using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration;

public class ServerCertificateConfig
{
    private X509Certificate2 _certificate;


    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    [JsonIgnore]
    public X509Certificate2 Certificate => _certificate ??= GetCertificate();

    // The file Kestrel's certificate is loaded from, and the password that opens it: the configured
    // certificate when it exists, otherwise the bundled self-signed default (no password). The default
    // ships in the repository with its private key, so IsBundledDefault tells callers that this
    // certificate proves nothing and must never become a trust anchor (Linux probe bundle, #1424).
    [JsonIgnore]
    public (string Path, string Password, bool IsBundledDefault) CertificateSource
    {
        get
        {
            var configured = Path.Combine(ServerConfig.ConfigPath, Name);

            return File.Exists(configured)
                ? (configured, string.IsNullOrEmpty(Key) ? null : Key, false)
                : (Path.Combine(ServerConfig.ExecutableDirectory, "default.server.pfx"), null, true);
        }
    }


    private X509Certificate2 GetCertificate()
    {
        var (path, password, _) = CertificateSource;

        return password is null ? new X509Certificate2(path) : new X509Certificate2(path, password);
    }
}