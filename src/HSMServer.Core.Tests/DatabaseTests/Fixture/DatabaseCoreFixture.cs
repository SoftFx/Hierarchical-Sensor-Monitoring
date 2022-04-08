using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.DatabaseTests.Fixture
{
    public class DatabaseCoreFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(DatabaseCoreTests);

        //Todo? Probably remove this
        private readonly string _firstProductName = nameof(_firstProductName);
        private readonly string _secondProductName = nameof(_secondProductName);
        private readonly string _thirdProductName = nameof(_thirdProductName);

        private readonly string _firstUserName = nameof(_firstUserName);
        private readonly string _secondUserName = nameof(_secondUserName);
        private readonly string _thirdUserName = nameof(_thirdUserName);

        private readonly string _configurationObjectName = nameof(_configurationObjectName);

        public Product FirstProduct { get; init; }
        public Product SecondProduct { get; init; }
        public Product ThirdProduct { get; init; }

        public User FirstUser { get; init; }
        public User SecondUser { get; init; }
        public User ThirdUser { get; init; }

        public RegistrationTicket Ticket { get; init; }

        public ConfigurationObject ConfigurationObject { get; init; }

        public DatabaseCoreFixture()
        {
            FirstProduct = DatabaseCoreFactory.CreateProduct(_firstProductName);
            SecondProduct = DatabaseCoreFactory.CreateProduct(_secondProductName);
            ThirdProduct = DatabaseCoreFactory.CreateProduct(_thirdProductName);

            FirstUser = DatabaseCoreFactory.CreateUser(_firstUserName);
            SecondUser = DatabaseCoreFactory.CreateUser(_secondUserName);
            ThirdUser = DatabaseCoreFactory.CreateUser(_thirdUserName);

            Ticket = DatabaseCoreFactory.CreateTicket();

            ConfigurationObject = DatabaseCoreFactory.CreateConfiguration(_configurationObjectName);
        }
    }
}
