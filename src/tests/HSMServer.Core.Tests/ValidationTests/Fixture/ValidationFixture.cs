using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using Moq;

namespace HSMServer.Core.Tests.ValidationTests
{
    internal sealed class ValidationFixture
    {
        public ValidationFixture()
        {
            var databaseMoq = new Mock<IDatabaseCore>();
            databaseMoq.Setup(d => d.GetConfigurationObject(It.IsAny<string>())).Returns<string>(null);

            var configLogger = CommonMoqs.CreateNullLogger<ConfigurationProvider>();
            IConfigurationProvider config = new ConfigurationProvider(databaseMoq.Object, configLogger);
        }
    }
}
