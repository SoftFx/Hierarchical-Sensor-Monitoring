﻿using HSMCommon;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using HSMServer.Extensions;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestUsersManager
    {
        private const string DefaultUserName = "default";

        private const string TestUserName = "TestUserName";
        private const string TestUserViewerName = "TestUserViewer";
        private const string TestUserManagerName = "TestUserManager";
        private const string TestUserNotAdminName = "TestUserNotAdmin";
        private const string TestUserAdminName = "TestUserAdmin";

        internal static User DefaultUser { get; } =
            new(DefaultUserName)
            {
                IsAdmin = true,
                Password = HashComputer.ComputePasswordHash(DefaultUserName),
            };

        internal static User TestUser { get; } =
            BuildUser(GenerateRandomProductRole());

        internal static User TestUserViewer { get; } =
            BuildUser(ProductRoleEnum.ProductViewer, name: TestUserViewerName);

        internal static User TestUserManager { get; } =
            BuildUser(ProductRoleEnum.ProductManager, name: TestUserManagerName);

        internal static User Admin { get; } =
            BuildUser(TestUserAdminName, true);

        internal static User NotAdmin { get; } =
            BuildUser(TestUserNotAdminName, false);

        internal static User GetEmptyUser()
        {
            return new User()
            {
                Name = string.Empty,
                Password = string.Empty
            };
        }
        internal static User BuildRandomUser() =>
            BuildUser(GenerateRandomProductRole(), RandomGenerator.GetRandomString());

        internal static User BuildUserWithRole(ProductRoleEnum productRole, string productId = null)
        {
            var user = BuildUser(isAdmin: false);

            user.ProductsRoles = new List<(Guid, ProductRoleEnum)>()
            {
                (productId.ToGuid(), productRole),
            };

            return user;
        }

        private static User BuildUser(string name = TestUserName, bool? isAdmin = null) =>
             new(name)
             {
                 IsAdmin = isAdmin ?? RandomGenerator.GetRandomBool(),
                 Password = HashComputer.ComputePasswordHash(name),
             };

        private static User BuildUser(ProductRoleEnum productRole, string name = TestUserName) =>
            new(name)
            {
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(name),
                ProductsRoles = new List<(Guid, ProductRoleEnum)>()
                {
                    (TestProductsManager.TestProduct.Id.ToGuid(), productRole)
                },
            };

        private static ProductRoleEnum GenerateRandomProductRole() =>
            (ProductRoleEnum)RandomGenerator.GetRandomInt(min: 0, max: 2);
    }
}
