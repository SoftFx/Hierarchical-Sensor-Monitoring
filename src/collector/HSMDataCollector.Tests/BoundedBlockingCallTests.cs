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

        [Fact]
        public void Parallel_calls_do_not_interfere_and_hung_calls_time_out_independently()
        {
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();

                    System.Threading.Tasks.Parallel.For(0, 6, i =>
                    {
                        if (i % 2 == 0)
                        {
                            outcomes.Add("value:" + BoundedBlockingCall.Run(() => i, "fast call " + i));
                        }
                        else
                        {
                            try
                            {
                                BoundedBlockingCall.Run(
                                    () =>
                                    {
                                        release.Wait();
                                        return i;
                                    },
                                    "hung call " + i,
                                    TimeSpan.FromMilliseconds(100));
                                outcomes.Add("unexpected:" + i);
                            }
                            catch (TimeoutException)
                            {
                                outcomes.Add("timeout:" + i);
                            }
                        }
                    });

                    Assert.Equal(3, System.Linq.Enumerable.Count(outcomes, o => o.StartsWith("value:", StringComparison.Ordinal)));
                    Assert.Equal(3, System.Linq.Enumerable.Count(outcomes, o => o.StartsWith("timeout:", StringComparison.Ordinal)));
                    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Parallel bounded calls should not serialize behind each other.");
                }
                finally
                {
                    release.Set();
                }
            }
        }
    }
}
