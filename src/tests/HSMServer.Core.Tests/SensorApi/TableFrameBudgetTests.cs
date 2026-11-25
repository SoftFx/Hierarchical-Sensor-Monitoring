using HSMServer.SensorApi.Sessions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // The table frame budget (#1845) without a server: who is granted, and in which order. Every
    // take that waits is a task observed for completion; nothing waits for time.
    public class TableFrameBudgetTests
    {
        private static readonly Guid KeyA = Guid.NewGuid();
        private static readonly Guid KeyB = Guid.NewGuid();
        private static readonly Guid KeyC = Guid.NewGuid();


        // A holds a take, a second take of A waits for it, and B, with room in the budget, is not
        // held back behind A's waiter: by the fast path and by the waiting one.
        [Fact]
        public async Task AWaiterOfAKeyThatHoldsATake_DoesNotHoldBackAnotherKey()
        {
            var budget = new TableFrameBudget(30);

            Assert.True(budget.TryTake(10, KeyA));

            var secondOfA = budget.TakeAsync(10, KeyA, CancellationToken.None);

            Assert.False(secondOfA.IsCompleted);
            Assert.Equal(1, budget.Waiting);

            Assert.True(budget.TryTake(10, KeyB));
            budget.Release(10, KeyB);

            var ofB = budget.TakeAsync(10, KeyB, CancellationToken.None);

            Assert.True(ofB.IsCompletedSuccessfully);
            Assert.Equal(20, budget.InUse);
            Assert.False(secondOfA.IsCompleted);
            Assert.True(budget.IsHeldBackOnlyByItsKey(10, KeyA));

            budget.Release(10, KeyA);

            await secondOfA.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(20, budget.InUse);
            Assert.Equal(0, budget.Waiting);
        }

        // A waiter of a free key that waits for the bytes is not overtaken by a later, smaller take,
        // even when that one would fit, nor while a waiter of a held key sits ahead of both.
        [Fact]
        public async Task AWaiterThatWaitsForTheBytes_IsNotOvertakenByALaterArrival()
        {
            var budget = new TableFrameBudget(10);

            Assert.True(budget.TryTake(6, KeyA));

            var secondOfA = budget.TakeAsync(1, KeyA, CancellationToken.None);
            var ofB = budget.TakeAsync(6, KeyB, CancellationToken.None);

            Assert.False(ofB.IsCompleted);
            Assert.False(budget.TryTake(4, KeyC));

            var ofC = budget.TakeAsync(4, KeyC, CancellationToken.None);

            Assert.False(ofC.IsCompleted);
            Assert.Equal(3, budget.Waiting);
            Assert.False(budget.IsHeldBackOnlyByItsKey(6, KeyB));

            budget.Release(6, KeyA);

            // In order: A's second take (1) and B (6) fit; C (4) does not fit behind them.
            await secondOfA.WaitAsync(TimeSpan.FromSeconds(10));
            await ofB.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(ofC.IsCompleted);
            Assert.Equal(7, budget.InUse);

            budget.Release(6, KeyB);

            await ofC.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(5, budget.InUse);
            Assert.Equal(0, budget.Waiting);
        }
    }
}
