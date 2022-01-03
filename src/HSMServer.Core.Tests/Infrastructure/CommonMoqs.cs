using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class CommonMoqs
    {
        private static readonly NullLoggerFactory _loggerFactory = new();


        internal static ILogger<T> CreateNullLogger<T>() => _loggerFactory.CreateLogger<T>();

        internal static ISensorsDataValidator CreateValidatorMockWithoutDatabase()
        {
            var databaseMoq = new Mock<IDatabaseAdapter>();
            databaseMoq.Setup(d => d.GetConfigurationObject(It.IsAny<string>())).Returns<string>(null);

            ILogger<ConfigurationProvider> configLogger = CreateNullLogger<ConfigurationProvider>();
            IConfigurationProvider config = new ConfigurationProvider(databaseMoq.Object, configLogger);

            var productManagerMoq = new Mock<IProductManager>();
            ILogger<SensorsDataValidator> validatorLogger = CreateNullLogger<SensorsDataValidator>();
            ISensorsDataValidator validator = new SensorsDataValidator(config, databaseMoq.Object,
                productManagerMoq.Object, validatorLogger);

            return validator;
        }
    }
}
