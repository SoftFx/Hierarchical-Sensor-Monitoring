using System;
using HSMServer.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Per-source, per-aligned-minute budget for failed-authentication security events:
    // the event write is bounded, authentication itself is never throttled, one source
    // never consumes another's budget, and the registry itself stays bounded.
    public sealed class ApiTokenInvalidAttemptLimiterTests
    {
        private static readonly DateTime Now = new(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc);

        private static ApiTokenInvalidAttemptLimiter Build(int limit, Func<DateTime> utcNow = null) =>
            new(new ApiTokensConfig { InvalidAttemptRateLimit = limit },
                NullLogger<ApiTokenInvalidAttemptLimiter>.Instance, utcNow ?? (() => Now));


        [Fact]
        public void WithinTheLimit_EveryAttemptIsRecorded()
        {
            var limiter = Build(limit: 3);

            for (var i = 0; i < 3; i++)
                Assert.True(limiter.TryAcquire("10.0.0.1:1234"));

            Assert.Equal(0, limiter.DroppedCount);
        }

        [Fact]
        public void OverTheLimit_AttemptsAreDroppedAndCounted()
        {
            var limiter = Build(limit: 2);

            Assert.True(limiter.TryAcquire("10.0.0.1:1234"));
            Assert.True(limiter.TryAcquire("10.0.0.1:1234"));
            Assert.False(limiter.TryAcquire("10.0.0.1:1234"));

            Assert.Equal(1, limiter.DroppedCount);
        }

        [Fact]
        public void AnotherSource_HasItsOwnBudget_NeverBlockedGlobally()
        {
            // "Invalid-attempt limiting is bounded and does not block valid users
            // globally": one source exhausting its budget leaves every other source —
            // including a source that has never failed before — fully budgeted.
            var limiter = Build(limit: 1);

            Assert.True(limiter.TryAcquire("10.0.0.1:1234"));
            Assert.False(limiter.TryAcquire("10.0.0.1:1234"));
            Assert.True(limiter.TryAcquire("10.0.0.2:5678"));
            Assert.False(limiter.TryAcquire("10.0.0.2:5678"));
        }

        [Fact]
        public void NullSource_SharesOneBucket_AndCannotBypassTheBound()
        {
            var limiter = Build(limit: 2);

            Assert.True(limiter.TryAcquire(null));
            Assert.True(limiter.TryAcquire(string.Empty));
            Assert.False(limiter.TryAcquire(null));
        }

        [Fact]
        public void WindowRollover_ResetsEverySourceBudget()
        {
            var clock = Now;
            var limiter = Build(limit: 1, () => clock);

            Assert.True(limiter.TryAcquire("10.0.0.1:1234"));
            Assert.False(limiter.TryAcquire("10.0.0.1:1234"));

            clock = Now.AddMinutes(1); // next aligned window

            Assert.True(limiter.TryAcquire("10.0.0.1:1234"));
        }

        [Fact]
        public void SourceRegistry_IsBounded_NewSourcesBeyondItAreDropped()
        {
            var limiter = Build(limit: 10);

            for (var i = 0; i < 1024; i++)
                Assert.True(limiter.TryAcquire($"10.0.{i / 256}.{i % 256}:1"));

            // The 1025th distinct source in one window is dropped — the map cannot grow
            // without bound under a spoofed-source flood.
            Assert.False(limiter.TryAcquire("192.168.0.1:9"));
            Assert.Equal(1, limiter.DroppedCount);
        }

        [Fact]
        public void Constructor_InvalidLimit_ThrowsWithTheConfigName()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => Build(limit: 0));

            Assert.Contains(nameof(ApiTokensConfig.InvalidAttemptRateLimit), ex.Message);
        }
    }
}
