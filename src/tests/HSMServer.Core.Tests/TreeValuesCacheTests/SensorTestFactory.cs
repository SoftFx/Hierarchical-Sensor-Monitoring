using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Shared builder for IntegerSensorModel tests over a mocked IDatabaseCore (the #1296 and
    /// #1328 suites) so the two do not drift apart.
    /// </summary>
    internal static class SensorTestFactory
    {
        internal static byte[] History(DateTime time, int value) =>
            new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = time, Status = SensorStatus.Ok, Value = value });

        internal static SensorEntity BuildEntity(bool isSingleton = false, TimeSpan? selfDestroyInterval = null) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = Guid.NewGuid().ToString(),
                DisplayName = RandomGenerator.GetRandomString(),
                Type = (byte)SensorType.Integer,
                IsSingleton = isSingleton,
                CreationDate = selfDestroyInterval.HasValue ? DateTime.UtcNow.AddMonths(-1).Ticks : 0L,
                Settings = selfDestroyInterval.HasValue
                    ? new Dictionary<string, TimeIntervalEntity>
                    {
                        [nameof(BaseSensorModel.Settings.SelfDestroy)] = new((long)TimeInterval.Ticks, selfDestroyInterval.Value.Ticks),
                    }
                    : null,
            };
    }
}
