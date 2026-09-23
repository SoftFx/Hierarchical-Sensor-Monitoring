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

    // Whether Certificate came from the bundled self-signed default.server.pfx. That file ships in the
    // repository with its private key, so its certificate proves nothing and must never become a trust
    // anchor (Linux probe bundle, #1424). Recorded when Certificate is loaded: Name/Key can change at
    // runtime but apply only after a restart, so this describes what Kestrel actually serves.
    [JsonIgnore]
    public bool IsBundledDefault { get; private set; }


    private X509Certificate2 GetCertificate()
    {
        var certificatePath = Path.Combine(ServerConfig.ConfigPath, Name);

        if (File.Exists(certificatePath))
        {
            IsBundledDefault = false;

            return string.IsNullOrEmpty(Key)
                ? new X509Certificate2(certificatePath)
                : new X509Certificate2(certificatePath, Key);
        }

        IsBundledDefault = true;

        return new X509Certificate2(Path.Combine(ServerConfig.ExecutableDirectory, "default.server.pfx"));
    }
}
