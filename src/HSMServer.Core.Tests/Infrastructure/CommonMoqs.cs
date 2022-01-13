using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class CommonMoqs
    {
        private static readonly NullLoggerFactory _loggerFactory = new();


        internal static ILogger<T> CreateNullLogger<T>() => _loggerFactory.CreateLogger<T>();
    }
}
