using HSMCommon;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class DatabaseAdapterUsersManager : DatabaseAdapterManager
    {
        private const string DefaultUserName = "default";
        private const string DefaultUserCertificateFileName = "default.client.crt";
        private const string DefaultUserCertificateThumbprint = "a563183e1fec784f45bc8f3aa47c40eba1a26df9";

        private const string TestUserName = "TestUserName";

        internal User DefaultUser { get; }

        internal User TestUser { get; }


        internal DatabaseAdapterUsersManager(string dbFolder) : base(dbFolder)
        {
            DefaultUser = GetDefaultUser();
            TestUser = GetTestUser();
        }


        private static User GetDefaultUser() =>
            new(DefaultUserName)
            {
                CertificateFileName = DefaultUserCertificateFileName,
                CertificateThumbprint = DefaultUserCertificateThumbprint,
                IsAdmin = true,
                Password = HashComputer.ComputePasswordHash(DefaultUserName),
            };

        private static User GetTestUser() =>
            new(TestUserName)
            {
                CertificateFileName = RandomValuesGenerator.GetRandomString(),
                CertificateThumbprint = RandomValuesGenerator.GetRandomString(40),
                IsAdmin = RandomValuesGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(TestUserName),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(DatabaseAdapterProductsManager.ProductName, (ProductRoleEnum)RandomValuesGenerator.GetRandomInt(min: 0, max: 1))
                },
            };
    }
}
