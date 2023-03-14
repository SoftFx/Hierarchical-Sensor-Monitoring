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
            TestUser(_defaultUser, _userManager[_defaultUser.Name]);
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

            await Task.WhenAll(users.Select(u => _userManager.AddUser(u.Name, u.Password, u.IsAdmin, u.ProductsRoles)));

            await FullTestUserAsync(users,
                                    _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Add user(s), Negative")]
        public async Task AddEmptyUserTest()
        {
            await _userManager.TryAdd(TestUsersManager.GetEmptyUser());

            var actual = _userManager.GetUsers();
            var expected = new List<User>(2) { TestUsersManager.DefaultUser, TestUsersManager.GetEmptyUser() };

            CompareUserLists(actual, expected);
        }

        [Fact]
        [Trait("Category", "Add user(s), Negative")]
        public async Task AddSameUserTest()
        {
            await _userManager.TryAdd(TestUsersManager.DefaultUser);

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

            await _userManager.UpdateUser(updatedUser);

            await FullTestUpdatedUserAsync(new() { updatedUser },
                                           new() { defaultUserFromDB },
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
            var users = await BuildAddAndGetRandomUsers(count - 1); // there is default user in db

            var updatedUsers = new List<User>(count);
            foreach (var user in users)
                updatedUsers.Add(BuildUpdatedUser(user));

            await Task.WhenAll(updatedUsers.Select(_userManager.UpdateUser));

            await FullTestUpdatedUserAsync(updatedUsers,
                                           users,
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
            foreach (var user in await BuildAddAndGetRandomUsers(count - 1)) // there is default user in db
                updatedUsers.Add(BuildUpdatedUser(user));

            _userManager.Updated += UpdateUserEvent;

            await Task.WhenAll(updatedUsers.Select(_userManager.UpdateUser));

            _userManager.Updated -= UpdateUserEvent;

            Assert.Equal(updatedUsers.Count, actualUpdatedUsers.Count);
            await FullTestUpdatedUserAsync(updatedUsers,
                                           actualUpdatedUsers,
                                           _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Update user(s)")]
        public async Task UpdateNonExistingUserTest()
        {
            await _userManager.UpdateUser(_testUser);

            await FullTestUserAsync(new() { _testUser },
                                    _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Remove user(s)")]
        public async Task RemoveUserByNameTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            await _userManager.RemoveUser(defaultUserFromDB.Name);

            await FullTestRemovedDefaultUserAsync(new() { defaultUserFromDB },
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
            var users = await BuildAddAndGetRandomUsers(count);

            await Task.WhenAll(users.Select(u => _userManager.RemoveUser(u.Name)));

            await FullTestRemovedDefaultUserAsync(users,
                                                  _databaseCoreManager.DatabaseCore.GetUsers);
        }

        [Fact]
        [Trait("Category", "Authenticate")]
        public async Task AuthenticateUserTest()
        {
            var defaultUserFromDB = await GetDefaultUserFromDB();

            var actual = _userManager.Authenticate(defaultUserFromDB.Name, defaultUserFromDB.Name);

            TestAuthenticateUser(defaultUserFromDB, actual);
        }

        [Fact]
        [Trait("Category", "Authenticate, Negative")]
        public void AuthenticateUnregisteredUserTest()
        {
            var unregisteredUser = new User() { Name = RandomGenerator.GetRandomString(), Password = RandomGenerator.GetRandomString() };

            var actual = _userManager.Authenticate(unregisteredUser.Name, unregisteredUser.Password);

            Assert.Null(actual);
        }

        [Fact]
        [Trait("Category", "Authenticate, Negative")]
        public void AuthenticateEmptyUserTest()
        {
            var unregisteredUser = new User() { Name = string.Empty, Password = string.Empty };

            var actual = _userManager.Authenticate(unregisteredUser.Name, unregisteredUser.Password);

            Assert.Null(actual);
        }

        [Fact]
        [Trait("Category", "Remove product from users")]
        public async Task RemoveProductFromUsersTest()
        {
            await AddUsers(TestUsersManager.TestUserViewer.Copy(), TestUsersManager.TestUserManager.Copy());

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
        public async Task GetViewersTest()
        {
            await AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetViewers(TestProductsManager.ProductId);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users, Negative")]
        public async Task GetEmptyViewersTest()
        {
            var emptyViewer = TestUsersManager.GetEmptyUser();
            emptyViewer.ProductsRoles = new(TestUsersManager.TestUserViewer.ProductsRoles);

            await _userManager.TryAdd(emptyViewer);

            var actual = _userManager.GetViewers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { emptyViewer };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public async Task GetManagersTest()
        {
            await AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetManagers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users, Negative")]
        public async Task GetEmptyManagersTest()
        {
            var emptyManager = TestUsersManager.GetEmptyUser();
            emptyManager.ProductsRoles = new(TestUsersManager.TestUserManager.ProductsRoles);

            await _userManager.TryAdd(emptyManager);

            var actual = _userManager.GetManagers(TestProductsManager.ProductId);
            var expected = new List<User>(1) { emptyManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public async Task GetOnlyAdminUsersTest()
        {
            static bool IsAdmin(User user) => user.IsAdmin;


            await AddUsers(TestUsersManager.Admin, TestUsersManager.NotAdmin);

            var actual = _userManager.GetUsers(IsAdmin);
            var expected = new List<User>(2) { TestUsersManager.DefaultUser, TestUsersManager.Admin };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public async Task GetUsersWithProductRoleTest()
        {
            static bool IsProductRole(User user) => user.ProductsRoles.Count > 0;


            await AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public async Task GetUsersWithNameTest()
        {
            static bool IsProductRole(User user) => user.Name == TestUsersManager.TestUserViewer.Name;


            await AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(1) { TestUsersManager.TestUserViewer };

            CompareUserLists(expected, actual);
        }

        [Fact]
        [Trait("Category", "Get users")]
        public async Task GetUsersOfProductTest()
        {
            static bool IsProductRole(User user) => user.ProductsRoles.Any(e => e.Item1 == TestProductsManager.ProductId);


            await AddUsers(TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager, TestUsersManager.Admin, TestUsersManager.NotAdmin);

            var actual = _userManager.GetUsers(IsProductRole);
            var expected = new List<User>(2) { TestUsersManager.TestUserViewer, TestUsersManager.TestUserManager };

            CompareUserLists(expected, actual);
        }

        private async Task FullTestUserAsync(List<User> expectedUsers, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            foreach (var expectedUser in expectedUsers)
            {
                TestUserByName(expectedUser);
                TestUserFromDB(expectedUser, getUsersFromDB);
            }
        }

        private void TestUserByName(User expected) =>
            TestUser(expected, _userManager[expected.Name]);

        private static void TestUserFromDB(User expected, GetAllUsersFromDB getUsersFromDB) =>
            TestUser(expected, new(getUsersFromDB().FirstOrDefault(u => u.UserName == expected.Name)));

        private async Task FullTestUpdatedUserAsync(List<User> expectedUsers, List<User> usersBeforeUpdate,
            GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            for (int i = 0; i < expectedUsers.Count; ++i)
            {
                TestUserByGuid(expectedUsers[i], usersBeforeUpdate[i]);
                TestUserByName(expectedUsers[i], usersBeforeUpdate[i]);
                TestUserFromDB(expectedUsers[i], usersBeforeUpdate[i], getUsersFromDB);
            }
        }

        private void TestUserByGuid(User expected, User userBeforeUpdate)
        {
            var userById = _userManager[userBeforeUpdate.Id];

            TestChangeableUserSettings(expected, userById);
            TestNotChangeableUserSettings(userBeforeUpdate, userById);
        }

        private void TestUserByName(User expected, User userBeforeUpdate)
        {
            var userByName = _userManager[userBeforeUpdate.Name];

            TestChangeableUserSettings(expected, userByName);
            TestNotChangeableUserSettings(userBeforeUpdate, userByName);
        }

        private static void TestUserFromDB(User expected, User userBeforeUpdate, GetAllUsersFromDB getUsersFromDB)
        {
            var userFromDB = getUsersFromDB().FirstOrDefault(u => u.Id == expected.Id);
            var actualUser = new User(userFromDB);

            TestChangeableUserSettings(expected, actualUser);
            TestNotChangeableUserSettings(userBeforeUpdate, actualUser);
        }


        private async Task FullTestRemovedDefaultUserAsync(List<User> removed, GetAllUsersFromDB getUsersFromDB)
        {
            await Task.Delay(100);

            foreach (var user in removed)
            {
                TestRemovedUserById(user);
                TestRemovedUserByName(user);
                TestRemovedUser(user, getUsersFromDB);
            }
        }

        private void TestRemovedUserById(User removedUser) =>
            Assert.Null(_userManager[removedUser.Id]);

        private void TestRemovedUserByName(User removedUser) =>
            Assert.Null(_userManager[removedUser.Name]);

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
            Assert.Equal(expected.Name, actual.Name);
        }

        private static void CompareUserLists(IEnumerable<User> expectedInput, IEnumerable<User> actualInput)
        {
            var expected = expectedInput.OrderBy(e => e.Name).ToList();
            var actual = actualInput.OrderBy(e => e.Name).ToList();

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

        private Task AddUsers(params User[] users) => Task.WhenAll(users.Select(_userManager.TryAdd));

        private async Task<List<User>> BuildAddAndGetRandomUsers(int count)
        {
            var users = BuildRandomUsers(count);

            await AddUsers(users.ToArray());

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
                //Id = source.Id,
                Name = GetUpdatedProperty(source.Name),
                //IsAdmin = !source.IsAdmin,
                Password = GetUpdatedProperty(source.Password),
                ProductsRoles = productRoles,
            };
        }

        private static string GetUpdatedProperty(object property) => $"{property}-updated";
    }
}