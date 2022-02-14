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
        private const string TestUserViewerName = "TestUserViewer";
        private const string TestUserManagerName = "TestUserManager";

        internal static User DefaultUser { get; } =
            new(DefaultUserName)
            {
                CertificateFileName = DefaultUserCertificateFileName,
                CertificateThumbprint = DefaultUserCertificateThumbprint,
                IsAdmin = true,
                Password = HashComputer.ComputePasswordHash(DefaultUserName),
            };

        internal static User TestUser { get; } =
            BuildUser((ProductRoleEnum)RandomGenerator.GetRandomInt(min: 0, max: 2));

        internal static User TestUserViewer { get; } =
            BuildUser(ProductRoleEnum.ProductViewer, name: TestUserViewerName);

        internal static User TestUserManager { get; } =
            BuildUser(ProductRoleEnum.ProductManager, name: TestUserManagerName);


        private static User BuildUser(ProductRoleEnum productRole, string name = TestUserName) =>
            new(name)
            {
                CertificateFileName = RandomGenerator.GetRandomString(),
                CertificateThumbprint = RandomGenerator.GetRandomString(40),
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(name),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(TestProductsManager.TestProduct.Key, productRole)
                },
            };
    }
}
