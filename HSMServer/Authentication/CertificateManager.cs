using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using HSMServer.Configuration;

namespace HSMServer.Authentication
{
    public class CertificateManager
    {
        //private static List<CertificateDescriptor> GetInstalledCertificates()
        //{
        //    List<CertificateDescriptor> result = new List<CertificateDescriptor>();
            
        //    X509Store x509Store = new X509Store("MAMS", StoreLocation.LocalMachine);
        //    x509Store.Open(OpenFlags.ReadOnly);

        //    foreach (X509Certificate2 certificate in x509Store.Certificates)
        //    {
        //        result.Add(new CertificateDescriptor(certificate.SubjectName.Name, certificate.HasPrivateKey, certificate.Thumbprint));
        //    }

        //    return result;
        //}

        public static IEnumerable<X509Certificate2> GetLocalCertificates()
        {
            string certFolder = Path.Combine(Config.ConfigFolderName, "Certificate");

            if(!Directory.Exists(certFolder))
                yield break;

            string[] files = Directory.GetFiles(certFolder, ".cer");

            foreach (var file in files)
            {
                X509Certificate2 certificate = null;

                try
                {
                    certificate = new X509Certificate2(file);
                }
                catch
                {
                    continue;
                }

                yield return certificate;
            }
        }


    }
}
