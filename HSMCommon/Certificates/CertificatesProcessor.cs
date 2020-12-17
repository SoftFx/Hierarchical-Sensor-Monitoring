using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace HSMCommon.Certificates
{
    public class CertificatesProcessor
    {
        #region private fields

        private static IList _orderedAttributes = new ArrayList
        {
            X509Name.E,
            X509Name.CN,
            X509Name.O,
            X509Name.C,
            X509Name.ST,
            X509Name.OU,
            X509Name.L
        };

        private static char[] _commaSeparator = new[] {','};
        private static char[] _equalsSeparator = new[] {'='};

        #endregion

        public static X509Certificate2 CreateUnsignedCertificate(CertificateData certificateData, bool addPrivateKey)
        {

            return new X509Certificate2();
        }

        public static X509Certificate2 CreateSignedCertificate(CertificateData certificateData, string keyFilePath)
        {
            return new X509Certificate2();
        }

        public static Org.BouncyCastle.X509.X509Certificate SignCertificate(Pkcs10CertificationRequest csr,
            X509Certificate2 caCertificate,
            string keyFilePath)
        {
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            X509V3CertificateGenerator certGen = new X509V3CertificateGenerator();

            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue),
                random);

            certGen.SetSerialNumber(serialNumber);
            certGen.SetIssuerDN(CreateIssuerName(caCertificate.IssuerName));
            certGen.SetNotBefore(DateTime.Today.Subtract(new TimeSpan(1,0,0,0)));
            certGen.SetNotAfter(DateTime.Today.AddDays(5000));
            certGen.SetSubjectDN(csr.GetCertificationRequestInfo().Subject);
            certGen.SetPublicKey(csr.GetPublicKey());

            AsymmetricKeyParameter privateKey = ImportPrivateKey(keyFilePath);

            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", privateKey, random);

            Org.BouncyCastle.X509.X509Certificate signedCertificate = certGen.Generate(signatureFactory);
            return signedCertificate;
        }
        public static X509Certificate2 CreateSelfSignedCertificate(CertificateData certificateData, bool addPrivateKey)
        {
            IDictionary attributes = GetNameParametersTable(certificateData);
            X509Name name = new X509Name(_orderedAttributes, attributes);

            var kpgen = new RsaKeyPairGenerator();
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            AsymmetricCipherKeyPair subjectKeyPair = default(AsymmetricCipherKeyPair);
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            kpgen.Init(keyGenerationParameters);
            subjectKeyPair = kpgen.GenerateKeyPair();
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", issuerKeyPair.Private, random);

            var cerKp = kpgen.GenerateKeyPair();

            X509V3CertificateGenerator certGen = new X509V3CertificateGenerator();

            certGen.SetSerialNumber(BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random));
            certGen.SetIssuerDN(name);
            certGen.SetNotBefore(DateTime.Today.Subtract(new TimeSpan(1, 0, 0, 0)));
            certGen.SetNotAfter(DateTime.Today.AddDays(5000));
            certGen.SetSubjectDN(name);
            certGen.SetPublicKey(cerKp.Public);
            certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            certGen.AddExtension(X509Extensions.AuthorityKeyIdentifier, true,
                new AuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(cerKp.Public)));

            var certificate = certGen.Generate(signatureFactory);

            if (addPrivateKey)
            {
                return AddPrivateKey(certificate, subjectKeyPair, random);
            }

            System.Security.Cryptography.X509Certificates.X509Certificate initialCert = DotNetUtilities.ToX509Certificate(certificate);
            return new X509Certificate2(initialCert);
        }

        public static X509Certificate2 AddPrivateKey(Org.BouncyCastle.X509.X509Certificate certificate,
            AsymmetricCipherKeyPair subjectKeyPair, SecureRandom random)
        {
            var store = new Pkcs12Store();
            string friendlyName = certificate.SubjectDN.ToString();
            X509Certificate2 cert = new X509Certificate2();
            var certificateEntry = new X509CertificateEntry(certificate);
            store.SetCertificateEntry(friendlyName, certificateEntry);
            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(subjectKeyPair.Private), new[] { certificateEntry });
            var stream = new MemoryStream();
            store.Save(stream, "".ToCharArray(), random);
            var convertedCertificate = new X509Certificate2(stream.ToArray(), "",
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            return convertedCertificate;
        }

        public static X509Certificate2 AddPrivateKey(Org.BouncyCastle.X509.X509Certificate certificate,
            AsymmetricCipherKeyPair subjectKeyPair)
        {
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            return AddPrivateKey(certificate, subjectKeyPair, random);
        }
        public static Pkcs10CertificationRequest CreateCertificateSignRequest(CertificateData certificateData, out AsymmetricCipherKeyPair subjectKP)
        {
            IDictionary attributes = GetNameParametersTable(certificateData);

            var kpgen = new RsaKeyPairGenerator();

            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);

            kpgen.Init(keyGenerationParameters);
            AsymmetricCipherKeyPair subjectKeyPair = default(AsymmetricCipherKeyPair);
            subjectKeyPair = kpgen.GenerateKeyPair();
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", issuerKeyPair.Private, random);

            X509Name subject = new X509Name(new ArrayList(attributes.Keys), attributes);

            Pkcs10CertificationRequest result = new Pkcs10CertificationRequest(signatureFactory, subject,
                subjectKeyPair.Public, null);

            subjectKP = subjectKeyPair;
            return result;
        }

        //public static void AddPrivateKeyToCertificate(Org.BouncyCastle.X509.X509Certificate certificate, )
        public static void ExportCrt(X509Certificate2 certificate, string filePath, string password = "")
        {
            byte[] certBytes = certificate.Export(X509ContentType.Cert, password);

            FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate);
            fs.Write(certBytes, 0, certBytes.Length);
            fs.Flush();
            fs.Close();
        }

        public static void ExportPfx(X509Certificate2 certificate, string filePath, string password = "")
        {
            byte[] certBytes = certificate.Export(X509ContentType.Pfx, password);

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

        #region Store methods
        public static void AddCertificateToTrustedRootCA(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            store.Close();
        }

        #endregion

        #region private methods

        private static IDictionary GetNameParametersTable(CertificateData data)
        {
            IDictionary attributes = new Hashtable();
            attributes[X509Name.E] = data.EmailAddress;
            attributes[X509Name.CN] = data.CommonName;
            attributes[X509Name.O] = data.OrganizationName;
            attributes[X509Name.C] = data.CountryName;
            attributes[X509Name.ST] = data.StateOrProvinceName;
            attributes[X509Name.OU] = data.OrganizationUnitName;
            attributes[X509Name.L] = data.LocalityName;
            return attributes;
        }
        private static X509Name CreateIssuerName(X500DistinguishedName name)
        {
            IDictionary attributes = new Hashtable();
            attributes[X509Name.E] = string.Empty;
            attributes[X509Name.CN] = string.Empty;
            attributes[X509Name.O] = string.Empty;
            attributes[X509Name.C] = string.Empty;
            attributes[X509Name.ST] = string.Empty;
            attributes[X509Name.OU] = string.Empty;
            attributes[X509Name.L] = string.Empty;
            var splitName = name.Name.Split(_commaSeparator);
            foreach (var param in splitName)
            {
                var res = param.Split(_equalsSeparator);
                switch (res[0].Trim())
                {
                    case "E":
                        attributes[X509Name.E] = res[1].Trim();
                        break;
                    case "CN":
                        attributes[X509Name.CN] = res[1].Trim();
                        break;
                    case "O":
                        attributes[X509Name.O] = res[1].Trim();
                        break;
                    case "C":
                        attributes[X509Name.C] = res[1].Trim();
                        break;
                    case "ST":
                        attributes[X509Name.ST] = res[1].Trim();
                        break;
                    case "OU":
                        attributes[X509Name.OU] = res[1].Trim();
                        break;
                    case "L":
                        attributes[X509Name.L] = res[1].Trim();
                        break;
                }
            }

            return new X509Name(_orderedAttributes, attributes);
        }

        private static AsymmetricKeyParameter ImportPrivateKey(string filePath)
        {
            StreamReader streamReader = new StreamReader(filePath);
            PemReader pemReader = new PemReader(streamReader);
            var keyPair = pemReader.ReadObject();

            AsymmetricKeyParameter keyParameter;
            AsymmetricCipherKeyPair cipherKeyPair = keyPair as AsymmetricCipherKeyPair;
            if (cipherKeyPair != null)
            {
                keyParameter = cipherKeyPair.Private;
            }
            else
            {
                keyParameter = keyPair as AsymmetricKeyParameter;
            }

            return keyParameter;
        }

        #endregion
    }
}
