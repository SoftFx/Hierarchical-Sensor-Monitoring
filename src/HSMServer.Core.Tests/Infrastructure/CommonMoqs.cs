using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class CommonMoqs
    {
        private static readonly ThreadLocal<NullLoggerFactory> _loggerFactory = new(() => new NullLoggerFactory());


        internal static ILogger<T> CreateNullLogger<T>() => _loggerFactory.Value.CreateLogger<T>();
    }
}
