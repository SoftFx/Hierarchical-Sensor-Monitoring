using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class UserManagerTests : IClassFixture<UserManagerFixture>
    {
        private readonly User _defaultUser;
        private readonly User _testUser;

        private readonly DatabaseAdapterUsersManager _databaseAdapterManager;
        private readonly UserManager _userManager;

        private delegate User GetUserByThumbprint(string thumbprint);
        private delegate User GetUserByUserName(string username);
        private delegate User GetUser(Guid id);
        private delegate List<User> GetAllUsersFromDB();


        public UserManagerTests(UserManagerFixture fixture)
        {
            _databaseAdapterManager = new DatabaseAdapterUsersManager(fixture.DatabasePath);
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _defaultUser = _databaseAdapterManager.DefaultUser;
            _testUser = _databaseAdapterManager.TestUser;

            _userManager = new UserManager(_databaseAdapterManager.DatabaseAdapter, CommonMoqs.CreateNullLogger<UserManager>());
        }


        [Fact]
        public async Task DefaultUserTest()
        {
            await Task.Delay(1000);

            var usersFromDB = _databaseAdapterManager.DatabaseAdapter.GetUsers();

            Assert.Single(usersFromDB);
            TestUser(_defaultUser, usersFromDB[0]);
            TestCachedUser(_defaultUser, _userManager.GetUserByCertificateThumbprint, _userManager.GetUserByUserName);
        }

        [Fact]
        public async Task AddUserTest()
        {
            _userManager.AddUser(_testUser.UserName, _testUser.CertificateThumbprint,
                _testUser.CertificateFileName, _testUser.Password,
                _testUser.IsAdmin, _testUser.ProductsRoles);

            await FullTestUserAsync(_testUser,
                                    _userManager.GetUserByCertificateThumbprint,
                                    _userManager.GetUserByUserName,
                                    _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        public async Task UpdateUserTest()
        {
            await Task.Delay(1000);

            var defaultUserFromDB = _databaseAdapterManager.DatabaseAdapter.GetUsers()[0];

            var updatedUser = new User()
            {
                Id = defaultUserFromDB.Id,
                UserName = GetUpdatedProperty(defaultUserFromDB.UserName),
                CertificateFileName = GetUpdatedProperty(defaultUserFromDB.CertificateFileName),
                CertificateThumbprint = GetUpdatedProperty(defaultUserFromDB.CertificateThumbprint),
                IsAdmin = !defaultUserFromDB.IsAdmin,
                Password = GetUpdatedProperty(defaultUserFromDB.Password),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>(_testUser.ProductsRoles),
            };

            _userManager.UpdateUser(updatedUser);

            await FullTestUpdatedUserAsync(updatedUser,
                                           defaultUserFromDB,
                                           _userManager.GetUser,
                                           _userManager.GetUserByCertificateThumbprint,
                                           _userManager.GetUserByUserName,
                                           _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        public async Task UpdateNonExistingUserTest()
        {
            _userManager.UpdateUser(_testUser);

            await FullTestUserAsync(_testUser,
                                    _userManager.GetUserByCertificateThumbprint,
                                    _userManager.GetUserByUserName,
                                    _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }


        private static async Task FullTestUserAsync(User expected, GetUserByThumbprint getUserByThumbprint,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            TestUserByThumbprint(expected, getUserByThumbprint);
            TestUserByName(expected, getUserByName);
            TestUserFromDB(expected, getUsersFromDB);
        }

        private static async Task FullTestUpdatedUserAsync(User expected, User userBeforeUpdate, GetUser getUser,
            GetUserByThumbprint getUserByThumbprint, GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            TestUserByGuid(expected, userBeforeUpdate, getUser);
            TestUserByThumbprint(expected, userBeforeUpdate, getUserByThumbprint);
            TestUserByName(expected, userBeforeUpdate, getUserByName);
            TestUserFromDB(expected, userBeforeUpdate, getUsersFromDB);
        }

        private static void TestCachedUser(User expected, GetUserByThumbprint getUserByThumbprint, GetUserByUserName getUserByName)
        {
            TestUserByThumbprint(expected, getUserByThumbprint);
            TestUserByName(expected, getUserByName);
        }


        private static void TestUserByThumbprint(User expected, GetUserByThumbprint getUserByThumbprint) =>
            TestUser(expected, getUserByThumbprint(expected.CertificateThumbprint));

        private static void TestUserByName(User expected, GetUserByUserName getUserByName) =>
            TestUser(expected, getUserByName(expected.UserName));

        private static void TestUserByGuid(User expected, GetUser getUser) =>
            TestUser(expected, getUser(expected.Id));

        private static void TestUserFromDB(User expected, GetAllUsersFromDB getUsersFromDB) =>
            TestUser(expected, getUsersFromDB().FirstOrDefault(u => u.UserName == expected.UserName));

        private static void TestUserByThumbprint(User expected, User userBeforeUpdate, GetUserByThumbprint getUserByThumbprint)
        {
            var userByThumbprint = getUserByThumbprint(userBeforeUpdate.CertificateThumbprint);

            TestChangeableUserSettings(expected, userByThumbprint);
            TestNotChangeableUserSettings(userBeforeUpdate, userByThumbprint);
        }

        private static void TestUserByName(User expected, User userBeforeUpdate, GetUserByUserName getUserByName)
        {
            var userByName = getUserByName(userBeforeUpdate.UserName);

            TestChangeableUserSettings(expected, userByName);
            TestNotChangeableUserSettings(userBeforeUpdate, userByName);
        }

        private static void TestUserByGuid(User expected, User userBeforeUpdate, GetUser getUser)
        {
            var userById = getUser(expected.Id);

            TestChangeableUserSettings(expected, userById);
            TestNotChangeableUserSettings(userBeforeUpdate, userById);
        }

        private static void TestUserFromDB(User expected, User userBeforeUpdate, GetAllUsersFromDB getUsersFromDB)
        {
            var userFromDB = getUsersFromDB().FirstOrDefault(u => u.Id == expected.Id);

            TestChangeableUserSettings(expected, userFromDB);
            TestNotChangeableUserSettings(userBeforeUpdate, userFromDB);
        }


        private static void TestUser(User expected, User actual)
        {
            TestChangeableUserSettings(expected, actual);
            TestNotChangeableUserSettings(expected, actual);            
        }

        private static void TestChangeableUserSettings(User expected, User actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Password, actual.Password);
            Assert.Equal(expected.IsAdmin, actual.IsAdmin);

            foreach (var productRole in expected.ProductsRoles)
            {
                var actualRole = actual.ProductsRoles.FirstOrDefault(r => r.Key == productRole.Key);

                Assert.Equal(productRole.Key, actualRole.Key);
                Assert.Equal(productRole.Value, actualRole.Value);
            }
        }

        private static void TestNotChangeableUserSettings(User expected, User actual)
        {
            Assert.Equal(expected.UserName, actual.UserName);
            Assert.Equal(expected.CertificateThumbprint, actual.CertificateThumbprint);
            Assert.Equal(expected.CertificateFileName, actual.CertificateFileName);
        }


        private static string GetUpdatedProperty(object property) => $"{property}-updated";
    }
}
