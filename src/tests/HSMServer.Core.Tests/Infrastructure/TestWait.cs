using System;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Infrastructure
{
    /// <summary>
    /// Bounded polling wait for an observable test precondition: async ingestion on a
    /// product update queue, the observable start of a Task.Run item. A fixed
    /// Wait(timeout) turns a merely late thread pool into a failure, while polling
    /// spends at most the timeout plus one late continuation. The timeout is still a
    /// deadline — each caller passes its own patience explicitly.
    /// </summary>
    public static class TestWait
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);


        public static async Task UntilAsync(Func<bool> condition, string message, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                    Assert.Fail($"Timed out waiting for: {message}");

                await Task.Delay(25);
            }
        }
    }
}
