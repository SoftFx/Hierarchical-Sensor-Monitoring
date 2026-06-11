using System;
using System.Threading;
using HSMDataCollector.Threading;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-B2: OS calls without their own timeout (GetInstanceNames, GetServices) are wrapped in
    /// BoundedBlockingCall so a corrupted counter registry or a hung SCM cannot stall sensor
    /// init/poll indefinitely.
    /// </summary>
    public sealed class BoundedBlockingCallTests
    {
        [Fact]
        public void Fast_call_returns_its_value()
        {
            Assert.Equal(42, BoundedBlockingCall.Run(() => 42, "fast call"));
        }

        [Fact]
        public void Hung_call_throws_timeout_instead_of_blocking_forever()
        {
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    var exception = Assert.Throws<TimeoutException>(() =>
                        BoundedBlockingCall.Run(
                            () =>
                            {
                                release.Wait();
                                return 0;
                            },
                            "hung call",
                            TimeSpan.FromMilliseconds(100)));

                    Assert.Contains("hung call", exception.Message);
                }
                finally
                {
                    release.Set(); // Unblock the abandoned worker so the test process stays clean.
                }
            }
        }

        [Fact]
        public void Failing_call_propagates_the_original_exception_type()
        {
            Assert.Throws<InvalidOperationException>(() =>
                BoundedBlockingCall.Run<int>(() => throw new InvalidOperationException("original"), "failing call"));
        }
    }
}
