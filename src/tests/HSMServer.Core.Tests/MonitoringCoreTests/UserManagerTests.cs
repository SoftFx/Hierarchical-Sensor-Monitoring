using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Authentication;
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

        private delegate User GetUserByUserName(string username);
        private delegate User GetUser(Guid id);
        private delegate List<UserEntity> GetAllUsersFromDB();


        public UserManagerTests(UserManagerFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Fact]
        [Trait("Category", "Default user")]
        public async Task DefaultUserTest()
        {
            var usersFromDB = await GetUsersFromDB();

            Assert.Single(usersFromDB);
            TestUser(_defaultUser, new(usersFromDB[0]));
            TestUserByName(_defaultUser, _userManager.GetUserByUserName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Add user(s)")]
        public async Task AddUserTest(int count)
        {
            var users = BuildRandomUsers(count);

            users.ForEach(u => _userManager.AddUser(u.UserName, u.Password, u.IsAdmin, u.ProductsRoles));

            await FullTestUserAsync(users,
                                    _userManager.GetUserByUserName,
                                    _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Add user(s), Negative")]
        public void AddEmptyUserTest()
        {
            _userManager.AddUser(TestUsersManager.GetEmptyUser());

            var actual = _userManager.GetUsers();
            var expected = new List<User>(2) { TestUsersManager.DefaultUser, TestUsersManager.GetEmptyUser() };

            CompareUserLists(actual, expected);
        }

        [Fact]
        [Trait("Category", "Add user(s), Negative")]
        public void AddSameUserTest()
        {
            _userManager.AddUser(TestUsersManager.DefaultUser);

            var actual = _userManager.GetUsers();
            var expected = new List<User>(2) { TestUsersManager.DefaultUser, TestUsersManager.DefaultUser };

            CompareUserLists(actual, expected);
        }

        [Fact]
        [Trait("Category", "Update user(s)")]
        public async Task UpdateDefaultUserTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            var updatedUser = BuildUpdatedUser(defaultUserFromDB);
            updatedUser.ProductsRoles = new List<(Guid, ProductRoleEnum)>(_testUser.ProductsRoles);

            _userManager.UpdateUser(updatedUser);

            await FullTestUpdatedUserAsync(new() { updatedUser },
                                           new() { defaultUserFromDB },
                                           _userManager.GetCopyUser,
                                           _userManager.GetUserByUserName,
                                           _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Update user(s)")]
        public async Task UpdateUsersTest(int count)
        {
            var users = BuildAddAndGetRandomUsers(count - 1); // there is default user in db

            var updatedUsers = new List<User>(count);
            foreach (var user in users)
                updatedUsers.Add(BuildUpdatedUser(user));

            updatedUsers.ForEach(_userManager.UpdateUser);

            await FullTestUpdatedUserAsync(updatedUsers,
                                           users,
                                           _userManager.GetCopyUser,
                                           _userManager.GetUserByUserName,
                                           _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Update user(s)")]
        public async Task UpdateUserEventTest(int count)
        {
            List<User> actualUpdatedUsers = new(count);
            void UpdateUserEvent(User user) => actualUpdatedUsers.Add(user);


            var updatedUsers = new List<User>(count);
            foreach (var user in BuildAddAndGetRandomUsers(count - 1)) // there is default user in db
                updatedUsers.Add(BuildUpdatedUser(user));

            _userManager.UpdateEvent += UpdateUserEvent;

            updatedUsers.ForEach(_userManager.UpdateUser);

            _userManager.UpdateEvent -= UpdateUserEvent;

            Assert.Equal(updatedUsers.Count, actualUpdatedUsers.Count);
            await FullTestUpdatedUserAsync(updatedUsers,
                                           actualUpdatedUsers,
                                           _userManager.GetCopyUser,
                                           _userManager.GetUserByUserName,
                                           _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Update user(s)")]
        public async Task UpdateNonExistingUserTest()
        {
            _userManager.UpdateUser(_testUser);

            await FullTestUserAsync(new() { _testUser },
                                    _userManager.GetUserByUserName,
                                    _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Remove user(s)")]
        public async Task RemoveUserByNameTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            await _userManager.RemoveUser(defaultUserFromDB.UserName);

            await FullTestRemovedDefaultUserAsync(new() { defaultUserFromDB },
                                                  _userManager.GetCopyUser,
                                                  _userManager.GetUserByUserName,
                                                  _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Remove user(s), Negative")]
        public async Task RemoveUserByIncorrectNameTest()
        {
            await _userManager.RemoveUser(RandomGenerator.GetRandomString());

            var expected = new List<User>(1) { TestUsersManager.DefaultUser };
            var actual = _userManager.GetUsers();

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Remove user(s), Negative")]
        public async Task RemoveUserByEmptyNameTest()
        {
            await _userManager.RemoveUser(string.Empty);

            var expected = new List<User>(1) { TestUsersManager.DefaultUser };
            var actual = _userManager.GetUsers();

            CompareUserLists(expected, actual);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Remove user(s)")]
        public async Task RemoveUsersByNameTest(int count)
        {
            var users = BuildAddAndGetRandomUsers(count);

            users.ForEach(u => _userManager.RemoveUser(u.UserName)); // TODO how to await removeuser??

            await FullTestRemovedDefaultUserAsync(users,
                                                  _userManager.GetCopyUser,
                                                  _userManager.GetUserByUserName,
                                                  _databaseCoreManager.DatabaseCore.GetUsers);
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
        [Trait("Category", "Authenticate, Negative")]
        public void AuthenticateUnregisteredUserTest()
        {
            var unregisteredUser = new User() { UserName = RandomGenerator.GetRandomString(), Password = RandomGenerator.GetRandomString() };

            var actual = _userManager.Authenticate(unregisteredUser.UserName, unregisteredUser.Password);

            Assert.Null(actual);
        }

        [Fact]
        [Trait("Category", "Authenticate, Negative")]
        public void AuthenticateEmptyUserTest()
        {
            var unregisteredUser = new User() { UserName = string.Empty, Password = string.Empty };

            var actual = _userManager.Authenticate(unregisteredUser.UserName, unregisteredUser.Password);

            Assert.Null(actual);
        }

        [Fact]
        [Trait("Category", "Remove product from users")]
        public void RemoveProductFromUsersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer.Copy(), TestUsersManager.TestUserManager.Copy());

            _valuesCache.RemoveProduct(TestProductsManager.ProductId);

            var result = _userManager.GetUsers().ToList();

            for (int i = 0; i < result.Count; i++)
            {
                var actual = result[i].ProductsRoles.FirstOrDefault(p => p.Item1 == TestProductsManager.ProductId);
                Assert.Equal(default, actual);
            }
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetViewersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetViewers(TestProductsManager.ProductId);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users, Negative")]
        public void GetEmptyViewersTest()
        {
            var emptyViewer = TestUsersManager.GetEmptyUser();
            emptyViewer.ProductsRoles = new(TestUsersManager.TestUserViewer.ProductsRoles);
            _userManager.AddUser(emptyViewer);

            var actual = _userManager.GetViewers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { emptyViewer };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetManagersTest()
        {
            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetManagers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users, Negative")]
        public void GetEmptyManagersTest()
        {
            var emptyManager = TestUsersManager.GetEmptyUser();
            emptyManager.ProductsRoles = new(TestUsersManager.TestUserManager.ProductsRoles);

            _userManager.AddUser(emptyManager);

            var actual = _userManager.GetManagers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { emptyManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetOnlyAdminUsersTest()
        {
            bool IsAdmin(User user) => user.IsAdmin;


            AddUsers(TestUsersManager.Admin, TestUsersManager.NotAdmin);

            var actual = _userManager.GetUsers(IsAdmin);
            var expected = new List<User>(2) { TestUsersManager.DefaultUser, TestUsersManager.Admin };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersWithProductRoleTest()
        {
            bool IsProductRole(User user) => user.ProductsRoles.Count > 0;


            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersWithNameTest()
        {
            bool IsProductRole(User user) => user.UserName == TestUsersManager.TestUserViewer.UserName;


            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(1) { TestUsersManager.TestUserViewer };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public void GetUsersOfProductTest()
        {
            bool IsProductRole(User user) => user.ProductsRoles.Any(e => e.Item1 == TestProductsManager.ProductId);


            AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager, TestUsersManager.Admin, TestUsersManager.NotAdmin);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        private static async Task FullTestUserAsync(List<User> expectedUsers,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            foreach (var expectedUser in expectedUsers)
            {
                TestUserByName(expectedUser, getUserByName);
                TestUserFromDB(expectedUser, getUsersFromDB);
            }
        }

        private static void TestUserByName(User expected, GetUserByUserName getUserByName) =>
            TestUser(expected, getUserByName(expected.UserName));

        private static void TestUserFromDB(User expected, GetAllUsersFromDB getUsersFromDB) =>
            TestUser(expected, new(getUsersFromDB().FirstOrDefault(u => u.UserName == expected.UserName)));

        private static async Task FullTestUpdatedUserAsync(List<User> expectedUsers, List<User> usersBeforeUpdate,
            GetUser getUser, GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            for (int i = 0; i < expectedUsers.Count; ++i)
            {
                TestUserByGuid(expectedUsers[i], usersBeforeUpdate[i], getUser);
                TestUserByName(expectedUsers[i], usersBeforeUpdate[i], getUserByName);
                TestUserFromDB(expectedUsers[i], usersBeforeUpdate[i], getUsersFromDB);
            }
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
            var actualUser = new User(userFromDB);

            TestChangeableUserSettings(expected, actualUser);
            TestNotChangeableUserSettings(userBeforeUpdate, actualUser);
        }


        private static async Task FullTestRemovedDefaultUserAsync(List<User> removed, GetUser getUser,
            GetUserByUserName getUserByName, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            foreach (var user in removed)
            {
                TestRemovedUser(user, getUserByName);
                TestRemovedUser(user, getUser);
                TestRemovedUser(user, getUsersFromDB);
            }
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
                    var actualRole = actual.ProductsRoles.FirstOrDefault(r => r.Item1 == productRole.Item1);
                    
                    Assert.Equal(productRole, actualRole);
                }
        }

        private static void TestNotChangeableUserSettings(User expected, User actual)
        {
            Assert.Equal(expected.UserName, actual.UserName);
        }

        private static void CompareUserLists(IEnumerable<User> expectedInput, IEnumerable<User> actualInput)
        {
            var expected = expectedInput.OrderBy(e => e.UserName).ToList();
            var actual = actualInput.OrderBy(e => e.UserName).ToList();

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < actual.Count; i++)
                TestUser(expected[i], actual[i]);
        }

        private async Task<User> GetDefaultUserFromDB()
        {
            var result = await GetUsersFromDB();

            return new(result[0]);
        }

        private async Task<List<UserEntity>> GetUsersFromDB()
        {
            await Task.Delay(200);

            return _databaseCoreManager.DatabaseCore.GetUsers();
        }

        private void AddUsers(params User[] users)
        {
            foreach (var user in users)
                _userManager.AddUser(user);
        }

        private List<User> BuildAddAndGetRandomUsers(int count)
        {
            var users = BuildRandomUsers(count);

            users.ForEach(_userManager.AddUser);

            return _userManager.GetUsers().ToList();
        }

        private static List<User> BuildRandomUsers(int count)
        {
            var users = new List<User>(count);

            for (int i = 0; i < count; ++i)
                users.Add(TestUsersManager.BuildRandomUser());

            return users;
        }

        private static User BuildUpdatedUser(User source)
        {
            var productRoles = new List<(Guid, ProductRoleEnum)>(source.ProductsRoles.Count);
            productRoles.AddRange(source.ProductsRoles);

            return new()
            {
                Id = source.Id,
                UserName = GetUpdatedProperty(source.UserName),
                IsAdmin = !source.IsAdmin,
                Password = GetUpdatedProperty(source.Password),
                ProductsRoles = productRoles,
            };
        }

        private static string GetUpdatedProperty(object property) => $"{property}-updated";
    }
}