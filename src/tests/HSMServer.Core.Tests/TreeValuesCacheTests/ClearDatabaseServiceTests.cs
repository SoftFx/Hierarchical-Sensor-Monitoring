using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests;

/// <summary>
/// Pins the ordering the #1328 self-destroy guard relies on: ClearDatabaseService must run
/// CheckSensorsHistoryAsync (which initializes every sensor) BEFORE the self-destroy sweeps.
/// Swapping the two awaits silently degrades the fix into "self-destroy never runs" with only
/// a log line as evidence — this test is what catches that edit.
/// </summary>
public class ClearDatabaseServiceTests
{
    [Fact]
    public async Task ServiceActionAsync_RunsHistoryCheckBeforeSelfDestroySweeps()
    {
        var calls = new List<string>();
        var cache = new Mock<ITreeValuesCache>();
        cache.Setup(c => c.CheckSensorsHistoryAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(nameof(ITreeValuesCache.CheckSensorsHistoryAsync)))
            .Returns(Task.CompletedTask);
        cache.Setup(c => c.RunSensorsSelfDestroyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(nameof(ITreeValuesCache.RunSensorsSelfDestroyAsync)))
            .Returns(Task.CompletedTask);
        cache.Setup(c => c.RunProductsSelfDestroyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(nameof(ITreeValuesCache.RunProductsSelfDestroyAsync)))
            .Returns(Task.CompletedTask);

        using var service = new ClearDatabaseService(cache.Object);

        var action = (Task)typeof(BaseDelayedBackgroundService)
            .GetMethod("ServiceActionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(service, new object[] { CancellationToken.None });
        await action;

        Assert.Equal(new[]
        {
            nameof(ITreeValuesCache.CheckSensorsHistoryAsync),
            nameof(ITreeValuesCache.RunSensorsSelfDestroyAsync),
            nameof(ITreeValuesCache.RunProductsSelfDestroyAsync),
        }, calls);
    }
}
