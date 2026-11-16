using HSMServer.SensorApi;
using System;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // The Error line of a registration fault is written once per window for the same fault
    // of the same key (#1749): a collector sends a package answered 500 again on every
    // cycle, for as long as the fault lasts. The clock is the test's.
    public class RepeatedFaultLogTests
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        private readonly Guid _key = Guid.NewGuid();

        private long _now;


        [Fact]
        public void TheSameFault_IsDueOncePerWindow_AndTheNextLineSaysWhatWentUnlogged()
        {
            var log = new RepeatedFaultLog(Window, capacity: 8, () => _now);

            Assert.True(log.IsDue(_key, Fault("the store is down"), out var first));
            Assert.Equal(0, first);

            // The collector sends the package again every 15 seconds.
            for (var cycle = 1; cycle <= 19; cycle++)
            {
                _now += 15_000;

                Assert.False(log.IsDue(_key, Fault("the store is down"), out _), $"cycle {cycle}, {_now} ms after the first line");
            }

            // 5 minutes after the first line.
            _now += 15_000;

            Assert.True(log.IsDue(_key, Fault("the store is down"), out var unlogged));
            Assert.Equal(19, unlogged);
            Assert.Equal(" 19 more request(s) with the same fault were not logged since the previous line.", RepeatedFaultLog.Unlogged(unlogged));

            // The count starts again with the new line.
            _now += 15_000;

            Assert.False(log.IsDue(_key, Fault("the store is down"), out _));

            _now += (long)Window.TotalMilliseconds;

            Assert.True(log.IsDue(_key, Fault("the store is down"), out var afterTheSecondLine));
            Assert.Equal(1, afterTheSecondLine);
        }

        [Fact]
        public void AnotherKey_AnotherType_AnotherMessage_AreOtherFaults()
        {
            var log = new RepeatedFaultLog(Window, capacity: 8, () => _now);

            Assert.True(log.IsDue(_key, Fault("the store is down"), out _));

            Assert.True(log.IsDue(Guid.NewGuid(), Fault("the store is down"), out _));
            Assert.True(log.IsDue(_key, new TimeoutException("the store is down"), out _));
            Assert.True(log.IsDue(_key, Fault("the queue is closed"), out _));

            // And each of them is now within its own window.
            Assert.False(log.IsDue(_key, Fault("the store is down"), out _));
            Assert.False(log.IsDue(_key, Fault("the queue is closed"), out _));
        }

        [Fact]
        public void NothingUnlogged_AddsNothingToTheLine()
        {
            Assert.Equal(string.Empty, RepeatedFaultLog.Unlogged(0));
        }

        // Every distinct fault costs an entry, and a fault's text can carry a sensor path, so
        // the number of entries has a limit. A fault that was forgotten is simply logged
        // again when it returns.
        [Fact]
        public void RemembersNoMoreThanItsCapacity_ForgettingExpiredFaultsFirst()
        {
            var log = new RepeatedFaultLog(Window, capacity: 3, () => _now);

            Assert.True(log.IsDue(_key, Fault("first"), out _));

            _now += (long)Window.TotalMilliseconds;

            Assert.True(log.IsDue(_key, Fault("second"), out _));
            Assert.True(log.IsDue(_key, Fault("third"), out _));
            Assert.Equal(3, log.Count);

            // No room: the fault whose window has passed goes, the other two stay.
            Assert.True(log.IsDue(_key, Fault("fourth"), out _));
            Assert.Equal(3, log.Count);
            Assert.False(log.IsDue(_key, Fault("second"), out _));
            Assert.False(log.IsDue(_key, Fault("third"), out _));

            // No room and nothing expired: the one logged longest ago goes.
            _now += 1;
            Assert.True(log.IsDue(_key, Fault("fifth"), out _));
            Assert.Equal(3, log.Count);

            // A flood of distinct faults never grows it.
            for (var i = 0; i < 1000; i++)
                Assert.True(log.IsDue(_key, Fault($"distinct {i}"), out _));

            Assert.Equal(3, log.Count);
        }


        private static InvalidOperationException Fault(string message) => new(message);
    }
}
