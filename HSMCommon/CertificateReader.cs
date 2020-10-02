using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace HSMCommon
{
    public static class CertificateReader
    {
        public static X509Certificate2 ReadCertificateFromPEMCertAndKey(string certFilePath, string keyFilePath)
        {
            try
            {
                StreamReader certStreamReader = new StreamReader(certFilePath);
                PemReader certPemReader = new PemReader(certStreamReader);
                Org.BouncyCastle.X509.X509Certificate certificate =
                    (Org.BouncyCastle.X509.X509Certificate) certPemReader.ReadObject();

                StreamReader keyStreamReader = new StreamReader(keyFilePath);
                PemReader keyPemReader = new PemReader(keyStreamReader);
                var keyPair = keyPemReader.ReadObject();

                AsymmetricKeyParameter keyParameter;
                if (keyPair is AsymmetricCipherKeyPair cipher)
                {
                    keyParameter = cipher.Private;
                }
                else
                {
                    keyParameter = keyPair as AsymmetricKeyParameter;
                }

                var store = new Pkcs12Store();
                string friendlyName = certificate.SubjectDN.ToString();
                var certificateEntry = new X509CertificateEntry(certificate);
                store.SetCertificateEntry(friendlyName, certificateEntry);
                store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(keyParameter),new[] {certificateEntry});

                var stream = new MemoryStream();
                store.Save(stream, "".ToArray(), new SecureRandom());

                var convertedCertificate = new X509Certificate2(stream.ToArray(), "", X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                return convertedCertificate;

            }
            catch (Exception e)
            {
                return new X509Certificate2();
            }
        }
    }
}
