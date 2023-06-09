using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    [Collection("Database collection")]
    public abstract class MonitoringCoreTestsBase<T> : IAsyncLifetime, IClassFixture<T> where T : DatabaseFixture
    {
        protected const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;

        private protected readonly DatabaseCoreManager _databaseCoreManager;


        protected readonly IUpdatesQueue _updatesQueue;
        protected readonly TreeValuesCache _valuesCache;
        protected readonly IUserManager _userManager;


        protected MonitoringCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture, bool addTestProduct = true)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            if (addTestProduct)
                _databaseCoreManager.AddTestProduct();
            dbRegisterFixture.RegisterDatabase(_databaseCoreManager);

            fixture.InitializeDatabase(_databaseCoreManager.DatabaseCore);

            _updatesQueue = new Mock<IUpdatesQueue>().Object;

            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _updatesQueue);

            var userManagerLogger = CommonMoqs.CreateNullLogger<UserManager>();
            _userManager = new UserManager(_databaseCoreManager.DatabaseCore, _valuesCache, userManagerLogger);
        }


        public Task InitializeAsync() => _userManager.Initialize();

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
