using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HSMCommon.Certificates
{
    public class CertificatesProcessor
    {
        public static X509Certificate2 CreateSelfSignedCertificate(CertificateData data)
        {
            var rsaKey = RSA.Create(2048);

            string subject = GetSubjectString(data);
            var certRequest = new CertificateRequest(subject, rsaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            certRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));
            certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, false));

            var expire = DateTimeOffset.Now.AddYears(15);
            var caCert = certRequest.CreateSelfSigned(DateTimeOffset.Now, expire);
            return caCert;
        }

        public static void ExportCrt(X509Certificate2 certificate, string filePath, string password = "")
        {
            byte[] certBytes = certificate.Export(X509ContentType.Cert, password);

            FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate);
            fs.Write(certBytes, 0, certBytes.Length);
            fs.Flush();
            fs.Close();
        }

        public static void ExportPEMPrivateKey(X509Certificate2 certificate, string filePath)
        {
            if (certificate.PrivateKey == null)
                return;

            var keyPair = DotNetUtilities.GetKeyPair(certificate.PrivateKey);
            using (TextWriter tw = new StreamWriter(filePath))
            {
                PemWriter pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(tw);
                pemWriter.WriteObject(keyPair.Private);
                tw.Flush();
                tw.Close();
            }
        }

        public static void InstallCertificate(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            store.Close();
        }

        public static void AddCertificateToTrustedRootCA(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            store.Close();
        }

        public static string GetSubjectString(CertificateData data)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(data.StateOrProvinceName))
            {
                sb.Append("ST = ");
                sb.Append(data.StateOrProvinceName);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.CommonName))
            {
                sb.Append("CN = ");
                sb.Append(data.CommonName);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.LocalityName))
            {
                sb.Append("L = ");
                sb.Append(data.LocalityName);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.OrganizationName))
            {
                sb.Append("O = ");
                sb.Append(data.OrganizationName);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.OrganizationUnitName))
            {
                sb.Append("OU = ");
                sb.Append(data.OrganizationUnitName);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.EmailAddress))
            {
                sb.Append("E = ");
                sb.Append(data.EmailAddress);
                sb.Append(", ");
            }

            if (!string.IsNullOrEmpty(data.CountryName))
            {
                sb.Append("C = ");
                sb.Append(data.CountryName);
                sb.Append(", ");
            }

            return sb.Remove(sb.Length - 2, 2).ToString();
        }
    }
}
