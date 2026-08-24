using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

/// <summary>
/// Pins the ordering the #1328 self-destroy guard relies on: ClearDatabaseService must RUN TO
/// COMPLETION CheckSensorsHistoryAsync (which initializes every sensor) BEFORE the self-destroy
/// sweeps. Swapping the two awaits silently degrades the fix into "self-destroy never runs";
/// dropping the await on the history check does the same while still invoking it first — both
/// edits are caught here.
/// </summary>
public class ClearDatabaseServiceTests
{
    private sealed class TestableClearDatabaseService(ITreeValuesCache cache) : ClearDatabaseService(cache)
    {
        public Task Run(CancellationToken token) => ServiceActionAsync(token);
    }

    [Fact]
    public async Task ServiceActionAsync_CompletesHistoryCheckBeforeSelfDestroySweeps()
    {
        var calls = new List<string>();
        var cache = new Mock<ITreeValuesCache>();

        // Completes asynchronously and records a completion marker, so a fire-and-forget
        // history check (invocation order still correct) fails the assertion.
        cache.Setup(c => c.CheckSensorsHistoryAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                calls.Add(nameof(ITreeValuesCache.CheckSensorsHistoryAsync));
                await Task.Yield();
                calls.Add("historyCheckCompleted");
            });
        cache.Setup(c => c.RunSensorsSelfDestroyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(nameof(ITreeValuesCache.RunSensorsSelfDestroyAsync)))
            .Returns(Task.CompletedTask);
        cache.Setup(c => c.RunProductsSelfDestroyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(nameof(ITreeValuesCache.RunProductsSelfDestroyAsync)))
            .Returns(Task.CompletedTask);

        using var service = new TestableClearDatabaseService(cache.Object);

        await service.Run(CancellationToken.None);

        Assert.Equal(new[]
        {
            nameof(ITreeValuesCache.CheckSensorsHistoryAsync),
            "historyCheckCompleted",
            nameof(ITreeValuesCache.RunSensorsSelfDestroyAsync),
            nameof(ITreeValuesCache.RunProductsSelfDestroyAsync),
        }, calls);
    }
}
