using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using HSMCommon;
using HSMServer.Model;
using NLog;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

namespace HSMServer.Configuration
{
    public class CertificateManager
    {
        private readonly Logger _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private readonly List<CertificateDescriptor> _certificates = new List<CertificateDescriptor>();
        private readonly DateTime _lastUpdate = DateTime.MinValue;

        public CertificateManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate manager initialized");
        }

        private IEnumerable<CertificateDescriptor> ReadUserCertificates()
        {
            string certFolderPath = Config.CertificatesFolderPath;

            if(!Directory.Exists(certFolderPath))
                yield break;

            string[] files = Directory.GetFiles(certFolderPath, "*.crt");
            foreach (var file in files)
            {
                X509Certificate2 cert = null;
                CertificateDescriptor descriptor = null;
                try
                {
                    cert = new X509Certificate2(file);
                    descriptor = new CertificateDescriptor {Certificate = cert, FileName = Path.GetFileName(file)};
                }
                catch
                {
                    continue;
                }

                yield return descriptor;
            }
        }

        private void UpdateCertificates()
        {
            if (DateTime.Now - _lastUpdate > _updateInterval)
            {
                _certificates.Clear();
                _certificates.AddRange(ReadUserCertificates());
            }
        }
        public List<CertificateDescriptor> GetUserCertificates()
        {
            UpdateCertificates();

            return _certificates;
        }

        public X509Certificate2 GetCertificateByFileName(string fileName)
        {
            UpdateCertificates();

            return _certificates.FirstOrDefault(d => d.FileName.Equals(fileName))?.Certificate;
        }

        private List<X509Certificate2> GetCertificatesFromStore()
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            return store.Certificates.Cast<X509Certificate2>().ToList();
        }

        public X509Certificate2 GenerateClientCertificate(GenerateClientCertificateModel model)
        {
            var kpgen = new RsaKeyPairGenerator();

            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            AsymmetricCipherKeyPair subjectKeyPair = default(AsymmetricCipherKeyPair);
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);

            kpgen.Init(keyGenerationParameters);
            subjectKeyPair = kpgen.GenerateKeyPair();
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA512WITHRSA", issuerKeyPair.Private, random);

            var cerKp = kpgen.GenerateKeyPair();

            IDictionary attributes = new Hashtable();
            attributes[X509Name.E] = model.EmailAddress;
            attributes[X509Name.CN] = model.CommonName;
            attributes[X509Name.O] = model.OrganizationName;
            attributes[X509Name.C] = model.CountryName;
            attributes[X509Name.ST] = model.StateOrProvinceName;
            attributes[X509Name.OU] = model.OrganizationUnitName;
            attributes[X509Name.L] = model.LocalityName;

            IList orderedAttributes = new ArrayList();
            orderedAttributes.Add(X509Name.E);
            orderedAttributes.Add(X509Name.CN);
            orderedAttributes.Add(X509Name.O);
            orderedAttributes.Add(X509Name.C);
            orderedAttributes.Add(X509Name.ST);
            orderedAttributes.Add(X509Name.OU);
            orderedAttributes.Add(X509Name.L);

            X509V3CertificateGenerator certGen = new X509V3CertificateGenerator();

            certGen.SetSerialNumber(BigInteger.One);
            certGen.SetIssuerDN(new X509Name(orderedAttributes, attributes));
            certGen.SetNotBefore(DateTime.Today.Subtract(new TimeSpan(1, 0, 0, 0)));
            certGen.SetNotAfter(DateTime.Today.AddDays(1000));
            certGen.SetSubjectDN(new X509Name(orderedAttributes, attributes));
            certGen.SetPublicKey(cerKp.Public);
            certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            certGen.AddExtension(X509Extensions.AuthorityKeyIdentifier, true,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(cerKp.Public)));

            var certificate = certGen.Generate(signatureFactory);

            var store = new Pkcs12Store();
            string friendlyName = certificate.SubjectDN.ToString();
            var certificateEntry = new X509CertificateEntry(certificate);
            store.SetCertificateEntry(friendlyName, certificateEntry);
            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(subjectKeyPair.Private), new[] { certificateEntry });
            var stream = new MemoryStream();
            store.Save(stream, "".ToCharArray(), random);
            var convertedCertificate = new X509Certificate2(stream.ToArray(), "",
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

            return convertedCertificate;
        }

        public void SaveClientCertificate(X509Certificate2 certificate, string fileName)
        {
            string certPath = Path.Combine(Config.CertificatesFolderPath, fileName);
            byte[] certBytes = certificate.Export(X509ContentType.Cert);

            FileStream fs = new FileStream(certPath, FileMode.CreateNew);
            fs.Write(certBytes, 0, certBytes.Length);
            fs.Flush();
            fs.Close();
        }

        public void InstallClientCertificate(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            store.Close();
        }

        public X509Certificate2 GetCrtCertificateFromPfx(X509Certificate2 pfxCert)
        {
            byte[] bytes = pfxCert.Export(X509ContentType.Cert, "");
            X509Certificate2 crtCert = new X509Certificate2(bytes);
            return crtCert;
        }
    }
}
