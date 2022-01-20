using HSMCommon;
using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class DatabaseAdapterUsersManager : DatabaseAdapterManager
    {
        private const string TestUserName = "TestUserName";

        internal User TestUser { get; }


        internal DatabaseAdapterUsersManager(string dbFolder) : base(dbFolder)
        {
            TestUser = GetTestUser();
        }


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
