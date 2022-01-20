using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class UserManagerTests : IClassFixture<UserManagerFixture>
    {
        private readonly User _testUser;

        private readonly DatabaseAdapterUsersManager _databaseAdapterManager;
        private readonly UserManager _userManager;


        public UserManagerTests(UserManagerFixture fixture)
        {
            _databaseAdapterManager = new DatabaseAdapterUsersManager(fixture.DatabasePath);
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _testUser = _databaseAdapterManager.TestUser;

            _userManager = new UserManager(_databaseAdapterManager.DatabaseAdapter, CommonMoqs.CreateNullLogger<UserManager>());
        }


        [Fact]
        public async Task AddUserTest()
        {
            _userManager.AddUser(_testUser.UserName, _testUser.CertificateThumbprint,
                _testUser.CertificateFileName, _testUser.Password,
                _testUser.IsAdmin, _testUser.ProductsRoles);

            await Task.Delay(2000);

            var usersFromDB = _databaseAdapterManager.DatabaseAdapter.GetUsers();

            var user = usersFromDB.FirstOrDefault(u => u.UserName == _testUser.UserName);

            Assert.NotNull(user);
            Assert.Equal(_testUser.UserName, user.UserName);
            Assert.Equal(_testUser.CertificateThumbprint, user.CertificateThumbprint);
            Assert.Equal(_testUser.CertificateFileName, user.CertificateFileName);
            Assert.Equal(_testUser.Password, user.Password);
            Assert.Equal(_testUser.IsAdmin, user.IsAdmin);
            foreach (var productRole in _testUser.ProductsRoles)
            {
                var actualRole = user.ProductsRoles.FirstOrDefault(r => r.Key == productRole.Key);
                Assert.Equal(productRole.Key, actualRole.Key);
                Assert.Equal(productRole.Value, actualRole.Value);
            }
        }
    }
}
