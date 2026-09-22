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
    // certificate when it exists, otherwise the bundled self-signed default (no password). The Linux
    // probe bundle reads the public chain from the same file (#1424).
    [JsonIgnore]
    public (string Path, string Password) CertificateSource
    {
        get
        {
            var configured = Path.Combine(ServerConfig.ConfigPath, Name);

            return File.Exists(configured)
                ? (configured, string.IsNullOrEmpty(Key) ? null : Key)
                : (Path.Combine(ServerConfig.ExecutableDirectory, "default.server.pfx"), null);
        }
    }


    private X509Certificate2 GetCertificate()
    {
        var (path, password) = CertificateSource;

        return password is null ? new X509Certificate2(path) : new X509Certificate2(path, password);
    }
}