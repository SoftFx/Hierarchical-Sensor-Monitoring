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
    public class UserManagerTests : MonitoringCoreTestsBase<UserManagerFixture>
    {
        private readonly User _defaultUser = TestUsersManager.DefaultUser;
        private readonly User _testUser = TestUsersManager.TestUser;

        private readonly UserManager _userManager;

        private delegate User GetUserByUserName(string username);
        private delegate User GetUser(Guid id);
        private delegate List<User> GetAllUsersFromDB();


        public UserManagerTests(UserManagerFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _userManager = new UserManager(_databaseAdapterManager.DatabaseAdapter, CommonMoqs.CreateNullLogger<UserManager>());
        }


        [Fact]
        [Trait("Category", "Default user")]
        public async Task DefaultUserTest()
        {
            var usersFromDB = await GetUsersFromDB();

            Assert.Single(usersFromDB);
            TestUser(_defaultUser, usersFromDB[0]);
            TestUserByName(_defaultUser, _userManager.GetUserByUserName);
        }

        [Fact]
        [Trait("Category", "One")]
        public async Task AddUserTest()
        {
            _userManager.AddUser(_testUser.UserName, _testUser.CertificateThumbprint,
                _testUser.CertificateFileName, _testUser.Password,
                _testUser.IsAdmin, _testUser.ProductsRoles);

            await FullTestUserAsync(_testUser,
                                    _userManager.GetUserByUserName,
                                    _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        [Trait("Category", "One")]
        public async Task UpdateUserTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

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
                                           _userManager.GetUserByUserName,
                                           _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        [Trait("Category", "One")]
        public async Task UpdateNonExistingUserTest()
        {
            _userManager.UpdateUser(_testUser);

            await FullTestUserAsync(_testUser,
                                    _userManager.GetUserByUserName,
                                    _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        [Trait("Category", "One")]
        public async Task RemoveUserByNameTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            _userManager.RemoveUser(defaultUserFromDB.UserName);

            await FullTestRemovedDefaultUserAsync(defaultUserFromDB,
                                                  _userManager.GetUser,
                                                  _userManager.GetUserByUserName,
                                                  _databaseAdapterManager.DatabaseAdapter.GetUsers);
        }

        [Fact]
        [Trait("Category", "Authenticate")]
        public async Task AuthenticateUserTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            var actual = _userManager.Authenticate(defaultUserFromDB.UserName, defaultUserFromDB.UserName);

            TestAuthenticateUser(defaultUserFromDB, actual);
        }

        [Fact]
        [Trait("Category", "Authenticate")]
        public void AuthenticateUnregisteredUserTest()
        {
            var UnregisteredUser = new User() { UserName = RandomGenerator.GetRandomString(), Password = RandomGenerator.GetRandomString() };

            var actual = _userManager.Authenticate(UnregisteredUser.UserName, UnregisteredUser.Password);

            Assert.Null(actual);
        }

        [Fact]
        [Trait("Category", "Remove product from users")]
        public void RemoveProductFromUsersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer.Copy(), TestUsersManager.TestUserManager.Copy());

            _userManager.RemoveProductFromUsers(TestProductsManager.TestProduct.Key);

            var result = _userManager.GetUsers().ToList();

            for (int i = 0; i < result.Count; i++)
            {
                var actual = result[i].ProductsRoles.FirstOrDefault(p => p.Key == TestProductsManager.TestProduct.Key);
                Assert.Equal(default, actual);
            }
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetViewiersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetViewers(TestProductsManager.TestProduct.Key).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager }.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetManagersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetManagers(TestProductsManager.TestProduct.Key).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.TestUserManager };

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetOnlyAdminUsersTest()
        {
            AddUsers(TestUsersManager.Admin, TestUsersManager.NotAdmin);

            bool IsAdmin(User user)
            {
                return user.IsAdmin;
            }

            var actual = _userManager.GetUsers(IsAdmin).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.DefaultUser, TestUsersManager.Admin }.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersWithProductRoleTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            bool IsProductRole(User user) => user.ProductsRoles.Count > 0;

            var actual = _userManager.GetUsers(IsProductRole).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager }.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]); ;
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersWithNameTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            bool IsProductRole(User user) => user.UserName == TestUsersManager.TestUserViewer.UserName;

            var actual = _userManager.GetUsers(IsProductRole).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.TestUserViewer }.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]); ;
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersOfProductTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager, TestUsersManager.Admin, TestUsersManager.NotAdmin);

            bool IsProductRole(User user) => user.ProductsRoles.Any(e => e.Key == TestProductsManager.TestProduct.Key);

            var actual = _userManager.GetUsers(IsProductRole).OrderBy(e => e.UserName).ToList();

            var expected = new List<User> { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager }.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]); ;
        }

        private void AddUsers(params User[] users)
        {
            foreach (var user in users)
                _userManager.AddUser(user);
        }

        private static async Task FullTestUserAsync(User expected,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            TestUserByName(expected, getUserByName);
            TestUserFromDB(expected, getUsersFromDB);
        }

        private static void TestUserByName(User expected, GetUserByUserName getUserByName) =>
            TestUser(expected, getUserByName(expected.UserName));

        private static void TestUserFromDB(User expected, GetAllUsersFromDB getUsersFromDB) =>
            TestUser(expected, getUsersFromDB().FirstOrDefault(u => u.UserName == expected.UserName));

        private static async Task FullTestUpdatedUserAsync(User expected, User userBeforeUpdate, GetUser getUser,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            TestUserByGuid(expected, userBeforeUpdate, getUser);
            TestUserByName(expected, userBeforeUpdate, getUserByName);
            TestUserFromDB(expected, userBeforeUpdate, getUsersFromDB);
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


        private static async Task FullTestRemovedDefaultUserAsync(User removed, GetUser getUser,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            TestRemovedUser(removed, getUserByName);
            TestRemovedUser(removed, getUser);
            TestRemovedUser(removed, getUsersFromDB);
        }

        private static void TestRemovedUser(User removedUser, GetUserByUserName getUserByName) =>
            Assert.Null(getUserByName(removedUser.UserName));

        private static void TestRemovedUser(User removedUser, GetUser getUser) =>
            TestUser(new User((User)null), getUser(removedUser.Id));

        private static void TestRemovedUser(User removedUser, GetAllUsersFromDB getUsersFromDB) =>
            Assert.Null(getUsersFromDB().FirstOrDefault(u => u.Id == removedUser.Id));


        private static void TestUser(User expected, User actual)
        {
            TestChangeableUserSettings(expected, actual);
            TestNotChangeableUserSettings(expected, actual);
        }

        private static void TestAuthenticateUser(User expected, User actual)
        {
            Assert.Null(actual.Password);
            Assert.Equal(expected.IsAdmin, actual.IsAdmin);
            Assert.Equal(expected.ProductsRoles, actual.ProductsRoles);
            TestNotChangeableUserSettings(expected, actual);
        }

        private static void TestChangeableUserSettings(User expected, User actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Password, actual.Password);
            Assert.Equal(expected.IsAdmin, actual.IsAdmin);

            if (expected.ProductsRoles != null)
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


        private async Task<User> GetDefaultUserFromDB()
        {
            var result = await GetUsersFromDB();

            return result[0];
        }

        private async Task<List<User>> GetUsersFromDB()
        {
            await Task.Delay(200);

            return _databaseAdapterManager.DatabaseAdapter.GetUsers();
        }

        private static string GetUpdatedProperty(object property) => $"{property}-updated";
    }
}