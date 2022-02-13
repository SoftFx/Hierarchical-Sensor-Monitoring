using HSMCommon;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestUsersManager
    {
        private const string DefaultUserName = "default";
        private const string DefaultUserCertificateFileName = "default.client.crt";
        private const string DefaultUserCertificateThumbprint = "a563183e1fec784f45bc8f3aa47c40eba1a26df9";

        private const string TestUserName = "TestUserName";

        internal static User DefaultUser { get; } =
            new(DefaultUserName)
            {
                CertificateFileName = DefaultUserCertificateFileName,
                CertificateThumbprint = DefaultUserCertificateThumbprint,
                IsAdmin = true,
                Password = HashComputer.ComputePasswordHash(DefaultUserName),
            };

        internal static User TestUser { get; } =
            new(TestUserName)
            {
                CertificateFileName = RandomGenerator.GetRandomString(),
                CertificateThumbprint = RandomGenerator.GetRandomString(40),
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(TestUserName),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(TestProductsManager.TestProduct.Key, (ProductRoleEnum)RandomGenerator.GetRandomInt(min: 0, max: 2))
                },
            };

        internal static User TestUserViewer { get; } =
            new(TestUserName)
            {
                CertificateFileName = RandomGenerator.GetRandomString(),
                CertificateThumbprint = RandomGenerator.GetRandomString(40),
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(TestUserName),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(TestProductsManager.TestProduct.Key, ProductRoleEnum.ProductViewer)
                },
            };

        internal static User TestUserManager { get; } =
            new(TestUserName)
            {
                CertificateFileName = RandomGenerator.GetRandomString(),
                CertificateThumbprint = RandomGenerator.GetRandomString(40),
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(TestUserName),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(TestProductsManager.TestProduct.Key, ProductRoleEnum.ProductManager)
                },
            };
    }
}
