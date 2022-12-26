using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Settings;

public class CertificateConfig
{
    public static string Name { get; set; }
    
    public static string Key { get; set; }
    
    public static X509Certificate2 GetCertificate()
    {
        var folderPath = System.Diagnostics.Debugger.IsAttached
            ? @$"{Environment.CurrentDirectory}\Config\"
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\app\Config"));

        return string.IsNullOrEmpty(Key)
            ? new X509Certificate2($"{folderPath}{Name}")
            : new X509Certificate2($"{folderPath}{Name}", Key);
    }
}