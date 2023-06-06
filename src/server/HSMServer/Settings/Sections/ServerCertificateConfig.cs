using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using HSMServer.Model;

namespace HSMServer.Settings;

public class ServerCertificateConfig
{
    private X509Certificate2 _certificate;


    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
    
    [JsonIgnore] 
    public X509Certificate2 Certificate => _certificate ??= GetCertificate();

    
    private X509Certificate2 GetCertificate()
    {
        var certificatePath = Path.Combine(ServerConfig.ConfigPath, Name);
        
        if (File.Exists(certificatePath))
        {
            return string.IsNullOrEmpty(Key)
                ? new X509Certificate2(certificatePath)
                : new X509Certificate2(certificatePath, Key);
        }
        
        return new X509Certificate2(Path.Combine(ServerConfig.ExecutableDirectory, "default.server.pfx"));
    }
}